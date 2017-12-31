using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;
using System.Diagnostics;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class ControlPortTests
    {
		[Fact]
		private static async Task IsCircuitEstablishedAsync()
		{
			var controlPortClient = new ControlPort.TorControlClient(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
			var yes = await controlPortClient.IsCircuitEstablishedAsync();
			Assert.True(yes);
		}

		[Fact]
	    private static async Task CanChangeCircuitMultipleTimesAsync()
	    {
		    var requestUri = "https://api.ipify.org/";

		    // 1. Get Tor IP
		    IPAddress currIp = await GetTorIpAsync(requestUri);

		    var controlPortClient = new ControlPort.TorControlClient(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
		    for (int i = 0; i < 5; i++)
		    {
			    IPAddress prevIp = currIp;
			    // Change Tor IP

			    await controlPortClient.ChangeCircuitAsync();

			    // Get changed Tor IP
			    currIp = await GetTorIpAsync(requestUri);

			    Assert.NotEqual(prevIp, currIp);
		    }
	    }

	    private static async Task<IPAddress> GetTorIpAsync(string requestUri)
	    {
		    var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);

			IPAddress torIp;
		    using (var httpClient = new HttpClient(handler))
		    {
			    var content =
				    await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
			    var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
			    Assert.True(gotIp);
		    }
		    return torIp;
	    }

	    [Fact]
	    private static async Task CanChangeCircuitAsync()
	    {
		    var requestUri = "https://api.ipify.org/";
		    IPAddress torIp;
		    IPAddress changedIp;

			// 1. Get Tor IP
		    var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler))
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			// 2. Change Tor IP
			var controlPortClient = new ControlPort.TorControlClient(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
			await controlPortClient.ChangeCircuitAsync();

			// 3. Get changed Tor IP
			var handler2 = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler2))
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				Assert.True(gotIp);
			}

		    Assert.NotEqual(changedIp, torIp);
	    }

		[Fact]
		private static async Task CanSendCustomCommandAsync()
		{
			var controlPortClient = new ControlPort.TorControlClient(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
			var res = await controlPortClient.SendCommandAsync("GETCONF SOCKSPORT");
			Assert.StartsWith("250 SocksPort", res);
		}

		[Fact]
		private static async Task CanChangeCircuitWithinSameHttpClientAsync()
		{
			var requestUri = "https://api.ipify.org/";
			IPAddress torIp;
			IPAddress changedIp;

			// 1. Get Tor IP
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync()
						;
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);

				// 2. Change Tor IP
				var controlPortClient = new ControlPort.TorControlClient(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
				await controlPortClient.ChangeCircuitAsync();
			
				// 3. Get changed Tor IP
				content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync()
						;
				gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				Assert.True(gotIp);
			}

			Assert.NotEqual(changedIp, torIp);
		}
	}
}
