using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.Exceptions;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class TorSocks5HandlerTests : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public TorSocks5HandlerTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
		}

		[Fact]
		public async Task CanDoBasicRequestAsync()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();

				Assert.Equal("\"Good question Holmes !\"", content);
			}
		}

		[Fact]
		public async Task CanReuseHandlerAsync()
		{
			// YOU HAVE TO SET THE HTTP CLIENT NOT TO DISPOSE THE HANDLER
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint);
			using (var httpClient = new HttpClient(handler, disposeHandler: false))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();

				Assert.Equal("\"Good question Holmes !\"", content);
			}
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();

				Assert.Equal("\"Good question Holmes !\"", content);
			}
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();

				Assert.Equal("\"Good question Holmes !\"", content);
			}
		}

		[Fact]
		public async Task CanRequestDifferentWithSameHandlerAsync()
		{
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message =
					await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future");
				var content = await message.Content.ReadAsStringAsync();
				Assert.Equal("\"Good question Holmes !\"", content);

				message = await httpClient.GetAsync("https://api.ipify.org/");
				content = await message.Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out IPAddress ip);
				Assert.True(gotIp);


				message = await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future");
				content = await message.Content.ReadAsStringAsync();
				Assert.Equal("\"Good question Holmes !\"", content);
			}
		}

		[Fact]
		private async Task TorIpIsNotTheRealOneAsync()
		{
			var requestUri = "https://api.ipify.org/";
			IPAddress realIp;
			IPAddress torIp;

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out realIp);
				Assert.True(gotIp);
			}

			// 2. Get Tor IP
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync()
						;
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			Assert.NotEqual(realIp, torIp);
		}

		[Fact]
		public async Task CanDoHttpsAsync()
		{
			var requestUri = "https://slack.com/api/api.test";
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();

				Assert.Equal("{\"ok\":true}", content);
			}
		}

		[Fact]
		public async Task CanDoIpAddressAsync()
		{
			var requestUri = "http://172.217.6.142";
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();

				Assert.NotEmpty(content);
			}
		}

		[Fact]
		public async Task CanRequestInRowAsync()
		{
			var firstRequest = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true";

			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				await (await httpClient.GetAsync(firstRequest)).Content.ReadAsStringAsync();
				await (await httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true")).Content.ReadAsStringAsync();
				await (await httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000")).Content.ReadAsStringAsync();
			}
		}

		[Fact]
		public async Task CanRequestInRowHttpsAsync()
		{
			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			{
				for (int i = 0; i < 2; i++)
				{
					using (var httpClient = new HttpClient(handler, disposeHandler: false))
					{
						await (await httpClient.GetAsync(
								"https://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true")
							).Content.ReadAsStringAsync();
					}
				}
			}
		}

		[Fact]
		public async Task ThrowsExcetpionsAsync()
		{
			await Assert.ThrowsAsync<TorException>(
				async () =>
				await new TorControlClient("127.0.0.1", 9054).ChangeCircuitAsync()
				);
			await Assert.ThrowsAsync<TorException>(
				async () =>
					await new TorControlClient(SharedFixture.HostAddress, SharedFixture.ControlPort, SharedFixture.ControlPortPassword + "a").ChangeCircuitAsync()
			);
		}

		[Fact]
		public async Task CanRequestOnionAsync()
		{
			var requestUri = "http://msydqstlz2kzerdg.onion/";

			using (var handler = new TorSocks5Handler(SharedFixture.TorSock5EndPoint))
			using (var httpClient = new HttpClient(handler))
			{
				var content =
					await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync()
						;

				Assert.Contains("Learn more about Ahmia and its team", content);
			}
		}
	}
}