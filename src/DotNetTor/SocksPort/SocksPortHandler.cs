using System;
using System.IO;
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
		private Uri _connectedTo = null;

		private Socket _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		private readonly IPEndPoint _socksEndPoint;

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
			try
			{
				return await TrySendAsync(request).ConfigureAwait(false);
			}
			catch (IOException ex)
				when (ex.InnerException is SocketException)
			{
				// Circuit has been changed, try again
				if (_tried < MaxTry)
					_tried++;
				else throw;

				if (_socket.Connected) _socket.Shutdown(SocketShutdown.Both);
				_socket.Dispose();
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_connecting = null;
				_connectingToDest = null;
				_connectedTo = null;
				return await TrySendAsync(request).ConfigureAwait(false);
			}
			catch (TorException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		private async Task<HttpResponseMessage> TrySendAsync(HttpRequestMessage request)
		{
			// CONNECT TO LOCAL TOR
			await EnsureConnected.ConfigureAwait(false);

			// CONNECT TO DOMAIN DESTINATION IF NOT CONNECTED ALREADY
			await EnsureConnectedToDestAsync(request).ConfigureAwait(false);

			await Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				await HttpSocketClient.SendRequestAsync(_stream, request).ConfigureAwait(false);
				HttpResponseMessage message =
					await HttpSocketClient.ReceiveResponseAsync(_stream, request).ConfigureAwait(false);

				_tried = 0;
				return message;
			}
			finally
			{
				Semaphore.Release();
			}
		}

		private Task _connectingToDest;
		private Stream _stream;
		private async Task EnsureConnectedToDestAsync(HttpRequestMessage request)
		{
			Uri uri = StripPath(request.RequestUri);
			if(_connectingToDest == null)
			{
				_connectingToDest = ConnectToDestAsync(uri);
			}
			await _connectingToDest.ConfigureAwait(false);
			if (_connectedTo.AbsoluteUri != uri.AbsoluteUri)
			{
				throw new TorException(
					$"Requests are only allowed to {_connectedTo.AbsoluteUri}, you are trying to connect to {uri.AbsoluteUri}");
			}
		}

		private static Uri StripPath(Uri requestUri)
		{
			var builder = new UriBuilder
			{
				Scheme = requestUri.Scheme,
				Port = requestUri.Port,
				Host = requestUri.Host
			};
			return builder.Uri;
		}

		private async Task ConnectToDestAsync(Uri uri)
		{
			var sendBuffer = Util.BuildConnectToUri(uri);
			await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
			var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
			var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
			Util.ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);
			_connectedTo = uri;
			Stream stream = new NetworkStream(_socket);
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
			_stream = stream;
		}

		private Task _connecting;
		private Task EnsureConnected => _connecting ?? (_connecting = ConnectAsync());

		private async Task ConnectAsync()
		{
			await _socket.ConnectAsync(_socksEndPoint).ConfigureAwait(false);

			// HANDSHAKE
			var sendBuffer = new ArraySegment<byte>(new byte[] { 5, 1, 0 });
			await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
			var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
			var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
			Util.ValidateHandshakeResponse(receiveBuffer, receiveCount);
		}

		private bool _disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				try
				{
					if (_socket.Connected)
						_socket.Shutdown(SocketShutdown.Both);
					_socket.Dispose();
				}
				catch (ObjectDisposedException)
				{
					return;
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