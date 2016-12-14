using System.Net;
using System.Net.Http;
using Xunit;

namespace DotNetTor.Tests
{
	// 1. Download TOR Expert Bundle: https://www.torproject.org/download/download
	// 2. Download the torrc config file sample: https://svn.torproject.org/svn/tor/tags/tor-0_0_9_5/src/config/torrc.sample.in
	// 3. Place torrc in the proper default location (depending on your OS) and edit it:
	//	- Uncomment the default ControlPort 9051
	//	- Uncomment and modify the password hash to HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6
	// 4. Run tor (it will run in the background and listen to the SocksPort 9050 and ControlPort 9051)
	// Now the tests should successfully run
	public class BasicTests
	{
		private const string HostAddress = "127.0.0.1";
		private const int SocksPort = 9050;
		private const int ControlPort = 9051;
		private const string ControlPortPassword = "ILoveBitcoin21";

		[Fact]
		public void CanDoBasicRequest()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}

		[Fact]
		private static void TorIpIsNotTheRealOne()
		{
			var requestUri = "http://icanhazip.com/";
			IPAddress realIp;
			IPAddress torIp;

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out realIp);
				Assert.True(gotIp);
			}

			// 2. Get TOR IP
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}
			}

			Assert.NotEqual(realIp, torIp);
		}

		[Fact]
		private static void CanChangeCircuit()
		{
			var requestUri = "http://icanhazip.com/";
			IPAddress torIp;
			IPAddress changedIp;

			// 1. Get TOR IP
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}

				// 2. Change TOR IP
				var controlPortClient = new ControlPort.Client(HostAddress, ControlPort, ControlPortPassword);
				controlPortClient.ChangeCircuit();

				// 3. Get changed TOR IP
				handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
					Assert.True(gotIp);
				}
			}

			Assert.NotEqual(changedIp, torIp);
		}

		[Fact]
		public void CanDoHttps()
		{
			var requestUri = "https://slack.com/api/api.test";
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

					Assert.Equal(content, "{\"ok\":true}");
				}
			}
		}

		[Fact]
		public void CanRequestALot()
		{
			string woTor;
			string wTor;
			var requestUri = "http://api.qbit.ninja/blocks/0000000000000000119fe3f65fd3038cbe8429ad2cf7c2de1e5e7481b34a01b4";
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					wTor = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				}
			}

			using (var httpClient = new HttpClient())
			{
				woTor = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
			}

			Assert.Equal(woTor, wTor);
		}
		[Fact]
		public void CanRequestInRow()
		{
			var firstRequest = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true";
			using (var socksPortClient = new SocksPort.Client(HostAddress, SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(firstRequest);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(firstRequest).Result.Content.ReadAsStringAsync().Result;
					content = httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true").Result.Content.ReadAsStringAsync().Result;
					content = httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000").Result.Content.ReadAsStringAsync().Result;
				}
			}
		}

		[Fact]
		public void ThrowsExcetpions()
		{
			Assert.Throws<TorException>(() => new SocksPort.Client("127.0.0.1", 9054));
			Assert.Throws<TorException>(() => new ControlPort.Client("127.0.0.1", 9054));
			Assert.Throws<TorException>(() => new ControlPort.Client(HostAddress, ControlPort, ControlPortPassword + "a"));
		}
	}
}
