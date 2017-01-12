using DotNetTor.SocksPort.Net;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort
{
	public class Client : IDisposable
	{
		private IPEndPoint _socksEndPoint;
		private Socks5Client _socks5Client;

		private Socket _socket2Server;
		private Socket _socket2Dest;

		public Client(string address = "127.0.0.1", int socksPort = 9050)
		{
			try
			{
				_socksEndPoint = new IPEndPoint(IPAddress.Parse(address), socksPort);
				_socks5Client = new Socks5Client();
			}
			catch (Exception ex)
			{
				throw new TorException("SocksPort client initialization failed.", ex);
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated + ": ConnectAsync()")]
		public NetworkHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
			=> ConnectAsync(domainName, requestType).Result; // Task.Result is fine, because the method is obsolated

		public async Task<NetworkHandler> ConnectAsync(string domainName, RequestType requestType)
		{
			await Util.AssertPortOpenAsync(_socksEndPoint).ConfigureAwait(false);
			try
			{
				int port = 0;
				if (requestType == RequestType.HTTP)
					port = 80;
				else if (requestType == RequestType.HTTPS)
					port = 443;

				_socket2Server = await _socks5Client.ConnectToServerAsync(_socksEndPoint).ConfigureAwait(false);
				_socket2Dest = _socks5Client.ConnectToDestination(_socket2Server, domainName, port);
				return new NetworkHandler(_socket2Dest);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated + ": ConnectAsync()")]
		public NetworkHandler GetHandlerFromRequestUri(string requestUri)
			=> ConnectAsync(requestUri).Result; // .Result is fine, because the method is obsolated

		public async Task<NetworkHandler> ConnectAsync(string requestUri)
		{
			try
			{
				Uri uri = new Uri(requestUri);
				_socket2Server = await _socks5Client.ConnectToServerAsync(_socksEndPoint).ConfigureAwait(false);
				_socket2Dest = _socks5Client.ConnectToDestination(_socket2Server, uri.DnsSafeHost, uri.Port);
				return new NetworkHandler(_socket2Dest);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		public void Dispose()
		{
			((IDisposable)_socket2Server).Dispose();
			((IDisposable)_socket2Dest).Dispose();
		}
	}
}