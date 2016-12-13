using DotNetTor.SocksPort.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort
{
	public class Client:IDisposable
	{
		private IPEndPoint _socksEndPoint;
		private Socks5Client _socks5Client;
		private Socket _socket2Server;

		public Client(string address = "127.0.0.1", int socksPort = 9050)
		{
			_socksEndPoint = new IPEndPoint(IPAddress.Parse(address), socksPort);
			_socks5Client = new Socks5Client();
			_socket2Server = _socks5Client.ConnectToServer(_socksEndPoint);
		}

		public NetworkHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
		{
			int port = 0;
			if (requestType == RequestType.HTTP)
				port = 80;
			else if (requestType == RequestType.HTTPS)
				port = 443;

			var socket2Dest = _socks5Client.ConnectToDestination(_socket2Server, domainName, port);
			return new NetworkHandler(socket2Dest);
		}
		public NetworkHandler GetHandlerFromRequestUri(string requestUri)
		{
			Uri uri = new Uri(requestUri);
			var socket2Dest = _socks5Client.ConnectToDestination(_socket2Server, uri.Host, uri.Port);
			return new NetworkHandler(socket2Dest);
		}

		public void Dispose()
		{
			((IDisposable)_socket2Server).Dispose();
		}
	}
}
