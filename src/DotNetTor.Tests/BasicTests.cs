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
	}
}
