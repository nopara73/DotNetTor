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

namespace DotNetTor.SocksPort
{

	public sealed class SocksPortHandler : HttpMessageHandler
	{
		// Tolerate errors
		private const int MaxRetry = 3;
		private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
		
		public readonly HttpProtocol Protocol = HttpProtocol.HTTP11;

		private static ConcurrentDictionary<string, SocksConnection> _Connections;
		
		public IPEndPoint EndPoint { get; private set; }

		private List<Uri> _References;
		
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
			_disposed = false;
			_References = new List<Uri>();
			_Connections = new ConcurrentDictionary<string, SocksConnection>();
			EndPoint = endpoint;

			ControlPort.Client.CircuitChangeRequested += Client_CircuitChangeRequested;
		}
		private void Reset()
		{
			Init(EndPoint);
		}

		private void Client_CircuitChangeRequested(object sender, EventArgs e)
		{
			Reset();
		}

		#endregion

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ctsToken)
		{			
			await Util.Semaphore.WaitAsync().ConfigureAwait(false);

			try
			{
				SocksConnection connection = null;
				try
				{
					Retry.Do(() =>
					{
						connection = ConnectToDestinationIfNotConnected(request.RequestUri);
					}, RetryInterval, MaxRetry);
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
			finally
			{
				Util.Semaphore.Release();
			}
		}

		#region DestinationConnections

		private SocksConnection ConnectToDestinationIfNotConnected(Uri uri)
		{
			uri = Util.StripPath(uri);
			lock (_Connections)
			{
				if (_Connections.TryGetValue(uri.AbsoluteUri, out SocksConnection connection))
				{
					if (!_References.Contains(uri))
					{
						connection.AddReference();
						_References.Add(uri);
					}
					return connection;
				}

				connection = new SocksConnection
				{
					EndPoint = EndPoint,
					Destination = uri
				};
				connection.AddReference();
				_References.Add(uri);
				_Connections.TryAdd(uri.AbsoluteUri, connection);
				return connection;
			}
		}

		#endregion
		
		#region Cleanup

		private void ReleaseUnmanagedResources()
		{
			lock (_Connections)
			{
				foreach (var reference in _References)
				{
					if (_Connections.TryGetValue(reference.AbsoluteUri, out SocksConnection connection))
					{
						connection.RemoveReference(out bool disposedSockets);
						if (disposedSockets)
						{
							_Connections.TryRemove(reference.AbsoluteUri, out connection);
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
