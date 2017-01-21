using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DotNetTor.SocksPort.Net;

namespace DotNetTor.SocksPort
{
	public sealed class SocksPortHandler : HttpMessageHandler
	{
		private Socket _socket = null;
		private IPEndPoint _socksEndPoint;

		public SocksPortHandler(string address = "127.0.0.1", int socksPort = 9050)
			: this(new IPEndPoint(IPAddress.Parse(address), socksPort))
		{

		}

		// ReSharper disable once MemberCanBePrivate.Global
		public SocksPortHandler(IPEndPoint endpoint)
		{
			_socksEndPoint = endpoint;
		}

		private const int MaxTry = 3;
		private int _tried = 0;
		private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			await Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				// CONNECT TO LOCAL TOR
				await EnsureConnectedToTorAsync().ConfigureAwait(false);
				
				return await TrySendAsync(request).ConfigureAwait(false);
			}
			finally
			{
				Semaphore.Release();
			}
		}

		private async Task<HttpResponseMessage> TrySendAsync(HttpRequestMessage request)
		{
			var uri = Util.StripPath(request.RequestUri);
			try
			{
				await ConnectToDestAsync(uri).ConfigureAwait(false);

				Stream stream;
				_connections.TryGetValue(uri, out stream);

				await HttpSocketClient.SendRequestAsync(stream, request).ConfigureAwait(false);
				HttpResponseMessage message =
					await HttpSocketClient.ReceiveResponseAsync(stream, request).ConfigureAwait(false);

				_tried = 0;
				return message;
			}
			catch (Exception ex)
			{
				// Circuit has been changed, try again
				// Or something else unexpected error happened, try again a few times
				if (_tried < MaxTry)
					_tried++;
				else throw new TorException(ex.Message, ex);

				await EnsureConnectedToTorAsync().ConfigureAwait(false);
				Stream stream;
				_connections.TryRemove(uri, out stream);
				return await TrySendAsync(request).ConfigureAwait(false);
			}
		}

		private readonly ConcurrentDictionary<Uri, Stream> _connections = new ConcurrentDictionary<Uri, Stream>();

		private async Task ConnectToDestAsync(Uri uri)
		{
			if (_connections.ContainsKey(uri)) return;

			var sendBuffer = Util.BuildConnectToUri(uri);
			await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
			var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
			var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);

			Util.ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);

			Stream stream = new NetworkStream(_socket, ownsSocket: false);
			if(uri.Scheme.Equals("https", StringComparison.Ordinal))
			{
				var httpsStream = new SslStream(stream);

				await httpsStream
					.AuthenticateAsClientAsync(
						uri.DnsSafeHost,
						new X509CertificateCollection(),
						SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
						checkCertificateRevocation: false)
					.ConfigureAwait(false);
				stream = httpsStream;
			}
			_connections.AddOrUpdate(uri, stream, (k, v) => stream);
		}

		private async Task EnsureConnectedToTorAsync()
		{
			if (_socket == null || _socket.Connected == false)
			{
				if(_socket != null)
					_socksPortUsers.AddOrUpdate(_socket, true, (k, v) => true);

				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				_socksPortUsers.AddOrUpdate(_socket, false, (k,v) => false);

				await _socket.ConnectAsync(_socksEndPoint).ConfigureAwait(false);

				// HANDSHAKE
				var sendBuffer = new ArraySegment<byte>(new byte[] {5, 1, 0});
				await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
				var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
				var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
				Util.ValidateHandshakeResponse(receiveBuffer, receiveCount);
			}
		}

		// bool can be disposed
		private static ConcurrentDictionary<Socket, bool> _socksPortUsers = new ConcurrentDictionary<Socket, bool>();

		private bool _disposed = false;

		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				_socksPortUsers.AddOrUpdate(_socket, true, (k, v) => true);
				if (_socksPortUsers.Values.All(x => x)) // if all socket let itself to be disposed so be it
				{
					foreach (Socket socket in _socksPortUsers.Keys)
					{
						try
						{
							if (socket.Connected)
								socket.Shutdown(SocketShutdown.Both);
							socket.Dispose();
						}
						catch (ObjectDisposedException)
						{
							continue;
						}
					}

					_socksPortUsers.Clear();
				}

				_disposed = true;
			}

			base.Dispose(disposing);
		}

		~SocksPortHandler()
		{
			Dispose(false);
		}
	}
}