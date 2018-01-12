using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class TorControlClientTests : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public TorControlClientTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
		}

		[Fact]
		private async Task IsCircuitEstablishedAsync()
		{
			var controlPortClient = new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword);
			var yes = await controlPortClient.IsCircuitEstablishedAsync();
			Assert.True(yes);
		}

		[Fact]
	    private async Task CanChangeCircuitMultipleTimesAsync()
	    {
		    var requestUri = "https://api.ipify.org/";

		    // 1. Get Tor IP
		    IPAddress currIp = await GetTorIpAsync(requestUri);

		    var controlPortClient = new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword);
		    for (int i = 0; i < 3; i++)
		    {
			    IPAddress prevIp = currIp;
			    // Change Tor IP

			    await controlPortClient.ChangeCircuitAsync();

			    // Get changed Tor IP
			    currIp = await GetTorIpAsync(requestUri);

			    Assert.NotEqual(prevIp, currIp);
		    }
	    }

	    private async Task<IPAddress> GetTorIpAsync(string requestUri)
	    {
			var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint);

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
	    private async Task CanChangeCircuitAsync()
	    {
		    var requestUri = "https://api.ipify.org/";
		    IPAddress torIp;
		    IPAddress changedIp;

			// 1. Get Tor IP
			var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint);
			using (var httpClient = new HttpClient(handler))
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			// 2. Change Tor IP
			var controlPortClient = new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword);
			await controlPortClient.ChangeCircuitAsync();

			// 3. Get changed Tor IP
			var handler2 = new TorSocks5Handler(SharedFixture.TorSock5EndPoint);
			using (var httpClient = new HttpClient(handler2))
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
				Assert.True(gotIp);
			}

		    Assert.NotEqual(changedIp, torIp);
	    }

		[Fact]
		private async Task CanSendCustomCommandAsync()
		{
			var controlPortClient = new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword);
			var res = await controlPortClient.SendCommandAsync("GETCONF SOCKSPORT");
			res = res.Replace('-', ' ');
			Assert.StartsWith("250 SocksPort", res);
		}

		[Fact]
		private async Task CanChangeCircuitWithinSameHttpClientAsync()
		{
			var requestUri = "https://api.ipify.org/";
			IPAddress torIp;
			IPAddress changedIp;

			// 1. Get Tor IP
			var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint);
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync()
						;
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);

				// 2. Change Tor IP
				var controlPortClient = new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword);
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
