using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class SocksPortTests
	{
		[Fact]
		public async Task CanDoBasicRequestAsync()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
				var content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.Equal(content, "\"Good question Holmes !\"");
			}
		}

		[Fact]
		public async Task CanReuseHandlerAsync()
		{
			// YOU HAVE TO SET THE HTTP CLIENT NOT TO DISPOSE THE HANDLER
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var httpClient = new HttpClient(handler, disposeHandler: false))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
				var content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.Equal(content, "\"Good question Holmes !\"");
			}
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
				var content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.Equal(content, "\"Good question Holmes !\"");
			}
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri).ConfigureAwait(false);
				var content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.Equal(content, "\"Good question Holmes !\"");
			}
		}

		[Fact]
		public async Task CanRequestDifferentWithSameHandlerAsync()
		{
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message =
					await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").ConfigureAwait(false);
				var content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);
				Assert.Equal(content, "\"Good question Holmes !\"");

				IPAddress ip;
				message = await httpClient.GetAsync("http://icanhazip.com/").ConfigureAwait(false);
				content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out ip);
				Assert.True(gotIp);


				message = await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").ConfigureAwait(false);
				content = await message.Content.ReadAsStringAsync().ConfigureAwait(false);
				Assert.Equal(content, "\"Good question Holmes !\"");
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
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync()
						.ConfigureAwait(false);
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			Assert.NotEqual(realIp, torIp);
		}

		[Fact]
		public async Task CanDoHttpsAsync()
		{
			var requestUri = "https://slack.com/api/api.test";
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync()
						.ConfigureAwait(false);

				Assert.Equal(content, "{\"ok\":true}");
			}
		}

		[Fact]
		public async Task CanRequestInRowAsync()
		{
			var firstRequest = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true";

			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				await (await httpClient.GetAsync(firstRequest).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				await (await httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
				await (await httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
			}
		}

		[Fact]
		public async Task CanRequestInRowHttpsAsync()
		{
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			{
				for (int i = 0; i < 2; i++)
				{
					using (var httpClient = new HttpClient(handler, disposeHandler: false))
					{
						await (await httpClient.GetAsync(
								"https://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true")
							.ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
					}
				}
			}
		}

		[Fact]
		public async Task ThrowsExcetpionsAsync()
		{
			await Assert.ThrowsAsync<TorException>(
				async () =>
				await new ControlPort.Client("127.0.0.1", 9054).ChangeCircuitAsync().ConfigureAwait(false)
				).ConfigureAwait(false);
			await Assert.ThrowsAsync<TorException>(
				async () =>
					await new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword + "a").ChangeCircuitAsync().ConfigureAwait(false)
			).ConfigureAwait(false);
		}

		[Fact]
		public async Task CanRequestOnionAsync()
		{
			var requestUri = "http://bitmixer2whesjgj.onion/order.php?addr1=16HGUokcXuJXn9yiV6uQ4N3umAWteE2cRR&pr1=33&time1=8&addr2=1F1Afwxr2xrs3ZQpf6ifqfNMxJWZt2JupK&pr2=67&time2=16&bitcode=AcOw&fee=0.6523";

			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri).ConfigureAwait(false)).Content.ReadAsStringAsync()
						.ConfigureAwait(false);

				Assert.Equal(content, "error=Invalid Bitcode");
			}
		}
	}
}