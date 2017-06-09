using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class ControlPortTests
    {
		//[Fact]
		//private static async Task CanGetInfoAsync()
		//{
		//	var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
		//	var getInfoRespone = controlPortClient.GetInfo();
		//}

		[Fact]
	    private static async Task CanChangeCircuitMultipleTimesAsync()
	    {
		    var requestUri = "http://icanhazip.com/";

		    // 1. Get TOR IP
		    IPAddress currIp = await GetTorIpAsync(requestUri).ConfigureAwait(false);

		    var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
		    for (int i = 0; i < 5; i++)
		    {
			    IPAddress prevIp = currIp;
			    // Change TOR IP

			    await controlPortClient.ChangeCircuitAsync().ConfigureAwait(false);

			    // Get changed TOR IP
			    currIp = await GetTorIpAsync(requestUri).ConfigureAwait(false);

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
				    await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
			    var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
			    Assert.True(gotIp);
		    }
		    return torIp;
	    }

	    [Fact]
	    private static async Task CanChangeCircuitAsync()
	    {
		    var requestUri = "http://icanhazip.com/";
		    IPAddress torIp;
		    IPAddress changedIp;

			// 1. Get TOR IP
		    var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler))
			{
				var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			// 2. Change TOR IP
			var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
			await controlPortClient.ChangeCircuitAsync().ConfigureAwait(false);

			// 3. Get changed TOR IP
			var handler2 = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler2))
			{
				var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				Assert.True(gotIp);
			}

		    Assert.NotEqual(changedIp, torIp);
	    }

		[Fact]
		private static async Task CanChangeCircuitWithinSameHttpClientAsync()
		{
			var requestUri = "http://icanhazip.com/";
			IPAddress torIp;
			IPAddress changedIp;

			// 1. Get TOR IP
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync()
						.ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);

				// 2. Change TOR IP
				var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
				await controlPortClient.ChangeCircuitAsync().ConfigureAwait(false);

				// 3. Get changed TOR IP

				content =
					await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync()
						.ConfigureAwait(false);
				gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				Assert.True(gotIp);
			}

			Assert.NotEqual(changedIp, torIp);
		}
	}
}
