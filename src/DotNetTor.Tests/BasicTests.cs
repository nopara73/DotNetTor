using DotNetTor.SocksPort.Net;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.ControlPort;
using Xunit;

namespace DotNetTor.Tests
{
	// 1. Download TOR Expert Bundle: https://www.torproject.org/download/download
	// 2. Download the torrc config file sample: https://svn.torproject.org/svn/tor/tags/tor-0_0_9_5/src/config/torrc.sample.in
	// 3. Place torrc in the proper default location (depending on your OS) and edit it:
	//	- Uncomment the default Shared.ControlPort 9051
	//	- Uncomment and modify the password hash to HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6
	// 4. Run tor (it will run in the background and listen to the SocksPort 9050 and Shared.ControlPort 9051)
	// Now the tests should successfully run
	public class BasicTests
	{
		[Fact]
		public async Task CanDoBasicRequestAsync()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync(requestUri).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);

					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}

		[Fact]
		private static async Task TorIpIsNotTheRealOneAsync()
		{
			var requestUri = "http://icanhazip.com/";
			IPAddress realIp;
			IPAddress torIp;

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out realIp);
				Assert.True(gotIp);
			}

			// 2. Get TOR IP
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync("icanhazip.com", RequestType.HTTP).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}
			}

			Assert.NotEqual(realIp, torIp);
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
				var handler = await socksPortClient.ConnectAsync("icanhazip.com", RequestType.HTTP).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}

				// 2. Change TOR IP
				var controlPortClient = new Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
				await controlPortClient.ChangeCircuitAsync().ConfigureAwait(false);

				// 3. Get changed TOR IP
				handler = await socksPortClient.ConnectAsync(requestUri).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
					Assert.True(gotIp);
				}
			}

			Assert.NotEqual(changedIp, torIp);
		}

		[Fact]
		public async Task CanDoHttpsAsync()
		{
			var requestUri = "https://slack.com/api/api.test";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync(requestUri).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);

					Assert.Equal(content, "{\"ok\":true}");
				}
			}
		}

		[Fact]
		public async Task CanRequestALotAsync()
		{
			string woTor;
			string wTor;
			var requestUri = "http://api.qbit.ninja/blocks/0000000000000000119fe3f65fd3038cbe8429ad2cf7c2de1e5e7481b34a01b4";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync(requestUri).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					wTor = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				}
			}

			using (var httpClient = new HttpClient())
			{
				woTor = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
			}

			Assert.Equal(woTor, wTor);
		}

		[Fact]
		public async Task CanRequestInRowAsync()
		{
			var firstRequest = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync(firstRequest).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					await (await httpClient.GetAsync(firstRequest).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					await (await httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					await (await httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				}
			}
		}

		[Fact]
		public async Task ThrowsExcetpionsAsync()
		{
			var client = new SocksPort.Client("127.0.0.1", 9054);

			await Assert.ThrowsAsync<TorException>(
				async()=>
				await client.ConnectAsync("icanhazip.com", RequestType.HTTP).ConfigureAwait(false)
				).ConfigureAwait(false);

			await Assert.ThrowsAsync<TorException>(
				async () =>
				await new Client("127.0.0.1", 9054).ChangeCircuitAsync().ConfigureAwait(false)
				).ConfigureAwait(false);
			await Assert.ThrowsAsync<TorException>(
				async () =>
					await new Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword + "a").ChangeCircuitAsync().ConfigureAwait(false)
			).ConfigureAwait(false);
		}

		[Fact]
		public async Task CanRequestOnionAsync()
		{
			var requestUri = "http://bitmixer2whesjgj.onion/order.php?addr1=16HGUokcXuJXn9yiV6uQ4N3umAWteE2cRR&pr1=33&time1=8&addr2=1F1Afwxr2xrs3ZQpf6ifqfNMxJWZt2JupK&pr2=67&time2=16&bitcode=AcOw&fee=0.6523";

			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = await socksPortClient.ConnectAsync(requestUri).ConfigureAwait(false);
				using (var httpClient = new HttpClient(handler))
				{
					var content = await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);

					Assert.Equal(content, "error=Invalid Bitcode");
				}
			}
		}
	}
}