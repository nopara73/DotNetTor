using DotNetTor.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Nito.AsyncEx;

namespace DotNetTor.SocksPort
{

	public sealed class SocksPortHandler : HttpMessageHandler
	{
		// Tolerate errors
		private const int MaxRetry = 3;
		private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
		
		public readonly HttpProtocol Protocol = HttpProtocol.HTTP11;

		private static ConcurrentDictionary<string, SocksConnection> _connections;
		private static AsyncLock _connectionsAsyncLock;
		
		public IPEndPoint EndPoint { get; private set; }

		private List<Uri> _references;
		
		private volatile bool _disposed;

		#region Constructors

		public SocksPortHandler(string address = "127.0.0.1", int socksPort = 9050)
		{
			Init(new IPEndPoint(IPAddress.Parse(address), socksPort));
		}

		public SocksPortHandler(IPEndPoint endpoint)
		{
			Init(endpoint);
		}

		private void Init(IPEndPoint endpoint)
		{
			_connectionsAsyncLock = new AsyncLock();
			_disposed = false;
			_references = new List<Uri>();
			_connections = new ConcurrentDictionary<string, SocksConnection>();
			EndPoint = endpoint;

			ControlPort.Client.CircuitChangeRequested += Client_CircuitChangeRequested;
		}
		private void Reset()
		{
			try
			{
				ReleaseUnmanagedResources();
			}
			catch (Exception)
			{
				// ignored
			}
			Init(EndPoint);
		}

		private void Client_CircuitChangeRequested(object sender, EventArgs e)
		{
			Reset();
		}

		#endregion

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ctsToken)
		{			
			using(await Util.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				SocksConnection connection = null;
				try
				{
					await Retry.DoAsync(() =>
					{
						connection = ConnectToDestinationIfNotConnected(request.RequestUri);
					}, RetryInterval, MaxRetry).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					throw new TorException("Failed to connect to the destination", ex);
				}
				ctsToken.ThrowIfCancellationRequested();

				// https://tools.ietf.org/html/rfc7230#section-2.7.1
				// A sender MUST NOT generate an "http" URI with an empty host identifier.
				if (request.RequestUri.DnsSafeHost == "") throw new HttpRequestException("Host identifier is empty");

				// https://tools.ietf.org/html/rfc7230#section-2.6
				// Intermediaries that process HTTP messages (i.e., all intermediaries
				// other than those acting as tunnels) MUST send their own HTTP - version
				// in forwarded messages.
				request.Version = Protocol.Version;

				try
				{
					return await connection.SendRequestAsync(request, ctsToken).ConfigureAwait(false);
				}
				catch(Exception ex)
				{
					if(ex is OperationCanceledException)
					{
						throw;
					}
					else
					{
						throw new TorException("Failed to send the request", ex);
					}
				}
			}
		}

		#region DestinationConnections

		private SocksConnection ConnectToDestinationIfNotConnected(Uri uri)
		{
			uri = Util.StripPath(uri);
			using (_connectionsAsyncLock.Lock())
			{
				if (_connections.TryGetValue(uri.AbsoluteUri, out SocksConnection connection))
				{
					if (!_references.Contains(uri))
					{
						connection.AddReference();
						_references.Add(uri);
					}
					return connection;
				}

				connection = new SocksConnection
				{
					EndPoint = EndPoint,
					Destination = uri
				};
				connection.AddReference();
				_references.Add(uri);
				_connections.TryAdd(uri.AbsoluteUri, connection);
				return connection;
			}
		}

		#endregion
		
		#region Cleanup

		private void ReleaseUnmanagedResources()
		{
			ControlPort.Client.CircuitChangeRequested -= Client_CircuitChangeRequested;
			using (_connectionsAsyncLock.Lock())
			{
				foreach (var reference in _references)
				{
					if (_connections.TryGetValue(reference.AbsoluteUri, out SocksConnection connection))
					{
						connection.RemoveReference(out bool disposedSockets);
						if (disposedSockets)
						{
							_connections.TryRemove(reference.AbsoluteUri, out connection);
						}
					}
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				try
				{
					ReleaseUnmanagedResources();
				}
				catch (Exception)
				{
					// ignored
				}

				_disposed = true;
			}

			base.Dispose(disposing);
		}
		~SocksPortHandler()
		{
			Dispose(false);
		}

		#endregion
	}
}
