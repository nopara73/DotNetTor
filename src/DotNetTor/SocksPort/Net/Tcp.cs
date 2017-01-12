using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Net
{
	internal class Tcp
	{
		private static readonly IDictionary<AddressFamily, string> AddressFamilyNames = new Dictionary<AddressFamily, string>
		{
			{ AddressFamily.InterNetwork, "IPv4" },
			{ AddressFamily.InterNetworkV6, "IPv6" }
		};

		public static async Task<Socket> ConnectToServerAsync(IPEndPoint endpoint, IEnumerable<AddressFamily> addressFamilies)
		{
			ValidateEndpoint(endpoint, addressFamilies);
			var tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
			return tcpClient.Client;
		}

		public static async Task<Socket> ConnectToServerAsync(string hostname, int port)
		{
			var tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(hostname, port).ConfigureAwait(false);
			return tcpClient.Client;
		}

		private static void ValidateEndpoint(IPEndPoint endpoint, IEnumerable<AddressFamily> addressFamilies)
		{
			// endpoints can't be null
			if (endpoint == null)
			{
				throw new ArgumentNullException(nameof(endpoint));
			}

			// validate address family
			var addressFamilyArray = addressFamilies.ToArray();
			if (!addressFamilyArray.Contains(endpoint.AddressFamily))
			{
				var message = new StringBuilder();
				message.AppendFormat("The IP endpoint address family '{0}' is not valid. The address family must be ", endpoint.AddressFamily);

				var names = new List<string>();
				for (int i = 0; i < addressFamilyArray.Length; i++)
				{
					var addressFamily = addressFamilyArray[i];
					string output;
					string name = AddressFamilyNames.TryGetValue(endpoint.AddressFamily, out output) ? $"{addressFamily} ({output})" : addressFamily.ToString();
					if (i == addressFamilyArray.Length - 1)
					{
						name = "or " + name;
					}

					names.Add(name);
				}

				message.Append(names.Count > 2 ? string.Join(", ", names) : string.Join(" ", names));
				message.Append(".");

				throw new ArgumentException(message.ToString(), nameof(endpoint));
			}
		}
	}
}