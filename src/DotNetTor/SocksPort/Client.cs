using DotNetTor.SocksPort.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;

namespace DotNetTor.SocksPort
{
	public sealed class Client : IDisposable
	{
		private readonly IPEndPoint _socksEndPoint;

		private Socket _socket;

		public Client(string address = "127.0.0.1", int socksPort = 9050)
		{
			try
			{
				_socksEndPoint = new IPEndPoint(IPAddress.Parse(address), socksPort);
			}
			catch (Exception ex)
			{
				throw new TorException("SocksPort client initialization failed.", ex);
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated + ": ConnectAsync()")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public NetworkHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
			=> ConnectAsync().Result; // Task.Result is fine, because the method is obsolated

		public async Task<NetworkHandler> ConnectAsync()
		{
			await Util.AssertPortOpenAsync(_socksEndPoint).ConfigureAwait(false);
			try
			{
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				await _socket.ConnectAsync(_socksEndPoint).ConfigureAwait(false);

				// HANDSHAKE
				var sendBuffer = new ArraySegment<byte>(new byte[] { 5, 1, 0 });
				await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
				var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
				var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
				Util.ValidateHandshakeResponse(receiveBuffer, receiveCount);

				return new NetworkHandler(_socket);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated + ":ConnectAsync()")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public NetworkHandler GetHandlerFromRequestUri(string requestUri = "")
		=> ConnectAsync().Result; // Task.Result is fine, because the method is obsolated

		private void ReleaseUnmanagedResources()
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
		}

		public void Dispose()
		{
			ReleaseUnmanagedResources();
			GC.SuppressFinalize(this);
		}

		~Client()
		{
			ReleaseUnmanagedResources();
		}
	}
}