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

		private Socket _socket;
		private readonly IPEndPoint _socksEndPoint;
		private readonly HttpSocketClient _httpSocketClient = new HttpSocketClient();

		public SocksPortHandler(string address = "127.0.0.1", int socksPort = 9050)
			: this(new IPEndPoint(IPAddress.Parse(address), socksPort))
		{

		}

		public SocksPortHandler(IPEndPoint endpoint)
		{
			_socksEndPoint = endpoint;
			_socket = _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		}

		private const int MaxTry = 3;
		private int _Tried = 0;
		private static readonly SemaphoreSlim _Semaphore = new SemaphoreSlim(1, 1);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			try
			{
				return await TrySendAsync(request).ConfigureAwait(false);
			}
			catch (IOException ex)
				when (
					ex.Message.Equals("Unable to read data from the transport connection: An established connection was aborted by the software in your host machine.", StringComparison.Ordinal)
					&&
					ex.InnerException != null
					&&
					ex.InnerException is SocketException
					&&
					ex.InnerException.Message.Equals("An established connection was aborted by the software in your host machine", StringComparison.Ordinal)
					)
			{
				// Circuit has been changed, try again
				if (_Tried < MaxTry)
					_Tried++;
				else throw;
				if (_socket.Connected) _socket.Shutdown(SocketShutdown.Both);
				_socket.Dispose();
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_Connecting = null;
				_ConnectingToDest = null;
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
			await EnsureConnected().ConfigureAwait(false);

			// CONNECT TO DOMAIN DESTINATION IF NOT CONNECTED ALREADY
			await EnsureConnectedToDestAsync(request).ConfigureAwait(false);

			await _Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				await _httpSocketClient.SendRequestAsync(_Stream, request).ConfigureAwait(false);
				HttpResponseMessage message =
					await _httpSocketClient.ReceiveResponseAsync(_Stream, request).ConfigureAwait(false);

				_Tried = 0;
				return message;
			}
			finally
			{
				_Semaphore.Release();
			}
		}

		private Task _ConnectingToDest;
		private Stream _Stream;
		private async Task EnsureConnectedToDestAsync(HttpRequestMessage request)
		{
			var uri = StripPath(request.RequestUri);
			if(_ConnectingToDest == null)
			{
				_ConnectingToDest = ConnectToDestAsync(uri);
			}
			await _ConnectingToDest.ConfigureAwait(false);
			if (_connectedTo.AbsoluteUri != uri.AbsoluteUri)
			{
				throw new TorException(
					$"Requests are only allowed to {_connectedTo.AbsoluteUri}, you are trying to connect to {uri.AbsoluteUri}");
			}
		}

		private Uri StripPath(Uri requestUri)
		{
			UriBuilder builder = new UriBuilder();
			builder.Scheme = requestUri.Scheme;
			builder.Port = requestUri.Port;
			builder.Host = requestUri.Host;
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
			_Stream = stream;
		}

		private Task _Connecting;
		private Task EnsureConnected()
		{
			if (_Connecting == null)
			{
				_Connecting = ConnectAsync();
			}
			return _Connecting;
		}

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