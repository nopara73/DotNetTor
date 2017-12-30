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

			ControlPort.TorControlClient.CircuitChangeRequested += Client_CircuitChangeRequested;
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

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
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
					ThrowIfFindsCancelException(ex, cancel);
					throw new TorException("Failed to connect to the destination", ex);
				}
				cancel.ThrowIfCancellationRequested();

				// https://tools.ietf.org/html/rfc7230#section-2.7.1
				// A sender MUST NOT generate an "http" URI with an empty host identifier.
				if (request.RequestUri.DnsSafeHost == "") throw new HttpRequestException("Host identifier is empty");

				// https://tools.ietf.org/html/rfc7230#section-2.6
				// Intermediaries that process HTTP messages (i.e., all intermediaries
				// other than those acting as tunnels) MUST send their own HTTP - version
				// in forwarded messages.
				request.Version = Protocol.Version;

				// if the user would try to retry its message, it makes sure it won't fail
				// it's a small performance hit, but doesn't matter until tens of megabytes are added to content on a slow computer
				// https://stackoverflow.com/a/46026230/2061103
				using (var clonedRequest = await request.CloneAsync())
				{
					try
					{
						return await connection.SendRequestAsync(clonedRequest, cancel).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						ThrowIfFindsCancelException(ex, cancel);
						throw new TorException("Failed to send the request", ex);
					}
				}
			}
		}

		private void ThrowIfFindsCancelException(Exception ex, CancellationToken cancel = default)
		{
			if (ex is OperationCanceledException)
			{
				throw ex;
			}
			if (ex is TaskCanceledException || ex is TimeoutException)
			{
				throw new OperationCanceledException(ex.Message, ex);
			}

			if (ex.InnerException != null)
			{
				ThrowIfFindsCancelException(ex.InnerException);
			}

			if (ex is AggregateException)
			{
				var aggrEx = ex as AggregateException;
				if (aggrEx.InnerExceptions != null)
				{
					foreach (var innerEx in aggrEx.InnerExceptions)
					{
						ThrowIfFindsCancelException(innerEx);
					}
				}
			}
			
			// if doesn't find OperationCanceledException
			cancel.ThrowIfCancellationRequested();
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
						connection.ReferenceCount++;
						_references.Add(uri);
					}
					return connection;
				}

				connection = new SocksConnection
				{
					EndPoint = EndPoint,
					Destination = uri
				};
				connection.ReferenceCount++;
				_references.Add(uri);
				_connections.TryAdd(uri.AbsoluteUri, connection);
				return connection;
			}
		}

		#endregion
		
		#region Cleanup

		private void ReleaseUnmanagedResources()
		{
			try
			{
				using (_connectionsAsyncLock.Lock())
				{
					ControlPort.TorControlClient.CircuitChangeRequested -= Client_CircuitChangeRequested;
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
			catch
			{
				// ignored
			}
		}

		protected override void Dispose(bool disposing)
		{
			try
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
			catch
			{
				// ignored
			}
		}
		~SocksPortHandler()
		{
			Dispose(false);
		}

		#endregion
	}
}
