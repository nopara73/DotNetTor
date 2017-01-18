using DotNetTor.SocksPort.Net;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace DotNetTor.Tests
{
	// See SocksPortTests.cs for proper TOR configuration
	public class ControlPortTests
    {
	    [Fact]
	    private static async Task CanChangeCircuitMultipleTimesAsync()
	    {
		    var requestUri = "http://icanhazip.com/";

		    using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
		    {
			    // 1. Get TOR IP
			    IPAddress currIp = await GetTorIpAsync(socksPortClient, requestUri).ConfigureAwait(false);
			    IPAddress prevIp;

			    var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
			    for (int i = 0; i < 5; i++)
			    {
				    prevIp = currIp;
				    // Change TOR IP

				    await controlPortClient.ChangeCircuitAsync().ConfigureAwait(false);

				    // Get changed TOR IP
				    currIp = await GetTorIpAsync(socksPortClient, requestUri).ConfigureAwait(false);

				    Assert.NotEqual(prevIp, currIp);
			    }

		    }
	    }

	    private static async Task<IPAddress> GetTorIpAsync(SocksPort.Client socksPortClient, string requestUri)
	    {
		    IPAddress torIp;
		    using (var handler = await socksPortClient.ConnectAsync().ConfigureAwait(false))
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
		    using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
		    {
			    var handler = await socksPortClient.ConnectAsync().ConfigureAwait(false);
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
			    handler = await socksPortClient.ConnectAsync().ConfigureAwait(false);
			    using (var httpClient = new HttpClient(handler))
			    {
				    var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				    var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				    Assert.True(gotIp);
			    }
		    }

		    Assert.NotEqual(changedIp, torIp);
	    }
    }
}
