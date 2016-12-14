using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetTor
{
    internal static class Util
    {
		internal static void AssertPortOpen(string address, int socksPort)
		{
			using (TcpClient tcpClient = new TcpClient())
			{
				try
				{
					tcpClient.ConnectAsync(address, socksPort).Wait();
				}
				catch (Exception ex)
				{
					throw new TorException($"{address}:{socksPort} is closed.", ex);
				}
			}
		}
	}
}
