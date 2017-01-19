using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotNetTor.SocksPort.Net;

namespace DotNetTor.SocksPort
{
	public sealed class SocksPortHandler : HttpMessageHandler
	{
		private Tuple<string, RequestType> _connectedTo = null;

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
		private static readonly SemaphoreSlim _Semaphore = new SemaphoreSlim(1,1);
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
			await EnsureConnectedToDest(request).ConfigureAwait(false);

			await _Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				var stream = await _httpSocketClient.GetStreamAsync(_socket, request).ConfigureAwait(false);
				await _httpSocketClient.SendRequestAsync(stream, request).ConfigureAwait(false);
				HttpResponseMessage message = await _httpSocketClient.ReceiveResponseAsync(stream, request).ConfigureAwait(false);

				_Tried = 0;
				return message;
			}
			finally
			{
				_Semaphore.Release();
			}
		}

		private Task _ConnectingToDest;
		private Task EnsureConnectedToDest(HttpRequestMessage request)
		{
			var uri = request.RequestUri;
			RequestType? reqType = null;
			if (uri.Port == 80)
				reqType = RequestType.HTTP;
			else if (uri.Port == 443)
				reqType = RequestType.HTTPS;
			if (reqType == null)
				throw new ArgumentException($"{nameof(uri.Port)} cannot be {uri.Port}");
			var connectedTo = new Tuple<string, RequestType>(uri.DnsSafeHost, (RequestType)reqType);
			if (_connectedTo == null)
			{
				if (_ConnectingToDest == null)
				{
					_ConnectingToDest = ConnectToDestAsync(uri, (RequestType)reqType, connectedTo);
				}
			}
			else if (!Equals(_connectedTo, connectedTo))
			{
				throw new TorException(
					$"Requests are only allowed to {_connectedTo.Item1} by {_connectedTo.Item2}, you are trying to connect to {connectedTo.Item1} by {connectedTo.Item2}");
			}
			return _ConnectingToDest;
		}
		private async Task ConnectToDestAsync(Uri uri, RequestType reqType, Tuple<string, RequestType> connectedTo)
		{
			var sendBuffer = Util.BuildConnectToDomainRequest(uri.DnsSafeHost, reqType);
			await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
			var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
			var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
			Util.ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);
			_connectedTo = connectedTo;
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