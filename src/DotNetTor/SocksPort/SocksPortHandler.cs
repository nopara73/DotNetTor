using System;
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

		private readonly Socket _socket;
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

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			try
			{
				if (!_socket.Connected)
				{
					// CONNECT TO LOCAL TOR
					await _socket.ConnectAsync(_socksEndPoint).ConfigureAwait(false);

					// HANDSHAKE
					var sendBuffer = new ArraySegment<byte>(new byte[] { 5, 1, 0 });
					await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
					var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
					var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
					Util.ValidateHandshakeResponse(receiveBuffer, receiveCount);
				}

				// CONNECT TO DOMAIN DESTINATION
				var uri = request.RequestUri;
				RequestType? reqType = null;
				if (uri.Port == 80)
					reqType = RequestType.HTTP;
				else if (uri.Port == 443)
					reqType = RequestType.HTTPS;
				if (reqType == null)
					throw new ArgumentException($"{nameof(uri.Port)} cannot be {uri.Port}");
				var connectedTo = new Tuple<string, RequestType>(uri.DnsSafeHost, (RequestType) reqType);
				if (_connectedTo == null)
				{
					var sendBuffer = Util.BuildConnectToDomainRequest(uri.DnsSafeHost, (RequestType) reqType);
					await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
					var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
					var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
					Util.ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);
					_connectedTo = connectedTo;
				}
				else if (!Equals(_connectedTo, connectedTo))
				{
					throw new TorException(
						$"Requests are only allowed to {_connectedTo.Item1} by {_connectedTo.Item2}, you are trying to connect to {connectedTo.Item1} by {connectedTo.Item2}");
				}

				var stream = await _httpSocketClient.GetStreamAsync(_socket, request).ConfigureAwait(false);
				await _httpSocketClient.SendRequestAsync(stream, request).ConfigureAwait(false);
				return await _httpSocketClient.ReceiveResponseAsync(stream, request).ConfigureAwait(false);
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