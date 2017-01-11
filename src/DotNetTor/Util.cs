using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetTor
{
    internal static class Util
    {
		internal static async Task AssertPortOpenAsync(IPEndPoint ipEndPoint)
		{
			using (TcpClient tcpClient = new TcpClient())
			{
				try
				{
					await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					throw new TorException($"{ipEndPoint.Address}:{ipEndPoint.Port} is closed.", ex);
				}
			}
		}
	}
}
