using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Net
{
	/// <summary>Gets a connected socket for the provided request.</summary>
	/// <param name="request">The HTTP request message.</param>
	/// <returns>The connected socket.</returns>
	public delegate Socket GetSocket(HttpRequestMessage request);

	/// <summary>Asynchronously gets a connected socket for the provided request.</summary>
	/// <param name="request">The HTTP request message.</param>
	/// <returns>The task resulting in the connected socket.</returns>
	public delegate Task<Socket> GetSocketAsync(HttpRequestMessage request);

	public class NetworkHandler : HttpMessageHandler
	{
		private Tuple<string, RequestType> _connectedTo = null;

		private readonly GetSocketAsync _getSocketAsync;
		private readonly HttpSocketClient _httpSocketClient;

		public NetworkHandler()
		{
			_httpSocketClient = new HttpSocketClient();
		}

		public NetworkHandler(Socket socket) : this()
		{
			_getSocketAsync = r => Task.FromResult(socket);
		}

		public NetworkHandler(GetSocket getSocket) : this()
		{
			_getSocketAsync = r => Task.FromResult(getSocket?.Invoke(r));
		}

		public NetworkHandler(GetSocketAsync getSocketAsync) : this()
		{
			_getSocketAsync = getSocketAsync;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Socket socket;
			if (_getSocketAsync != null)
			{
				socket = await _getSocketAsync(request).ConfigureAwait(false);
			}
			else
			{
				throw new TorException("Socket cannot be found");
				//socket = await Tcp.ConnectToServerAsync(request.RequestUri.DnsSafeHost, request.RequestUri.Port).ConfigureAwait(false);
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
				await socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
				var receiveBuffer = new ArraySegment<byte>(new byte[socket.ReceiveBufferSize]);
				var receiveCount = await socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
				Util.ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);
				_connectedTo = connectedTo;
			}
			else if (!Equals(_connectedTo, connectedTo))
			{
				throw new TorException(
					$"Requests are only allowed to {_connectedTo.Item1} by {_connectedTo.Item2}, you are trying to connect to {connectedTo.Item1} by {connectedTo.Item2}");
			}

			var stream = await _httpSocketClient.GetStreamAsync(socket, request).ConfigureAwait(false);

			await _httpSocketClient.SendRequestAsync(stream, request).ConfigureAwait(false);

			return await _httpSocketClient.ReceiveResponseAsync(stream, request).ConfigureAwait(false);
		}
	}
}