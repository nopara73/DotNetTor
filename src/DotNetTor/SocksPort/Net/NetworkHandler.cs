using System;
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

			var stream = await _httpSocketClient.GetStreamAsync(socket, request).ConfigureAwait(false);

			await _httpSocketClient.SendRequestAsync(stream, request).ConfigureAwait(false);

			return await _httpSocketClient.ReceiveResponseAsync(stream, request).ConfigureAwait(false);
		}
	}
}