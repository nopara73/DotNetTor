using System.Net;
using System.Net.Http;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
#pragma warning disable CS0618 // Type or member is obsolete
	public class ObsolateTests
	{
		[Fact]
		public void CanDoBasicRequest()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
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
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
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
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}

				// 2. Change TOR IP
				var controlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);

				controlPortClient.ChangeCircuit();

				// 3. Get changed TOR IP
				handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					string content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					bool gotIp = IPAddress.TryParse(content.Replace("\n", ""), out changedIp);
					Assert.True(gotIp);
				}
			}

			Assert.NotEqual(changedIp, torIp);
		}

		[Fact]
		public void CanDoHttps()
		{
			var requestUri = "https://slack.com/api/api.test";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
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
		public void CanRequestInRow()
		{
			var firstRequest = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(firstRequest);
				using (var httpClient = new HttpClient(handler))
				{
					httpClient.GetAsync(firstRequest).Result.Content.ReadAsStringAsync().Wait();
					httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true").Result.Content.ReadAsStringAsync().Wait();
					httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000").Result.Content.ReadAsStringAsync().Wait();
				}
			}
		}

		[Fact]
		public void CanRequestOnion()
		{
			var requestUri = "http://bitmixer2whesjgj.onion/order.php?addr1=16HGUokcXuJXn9yiV6uQ4N3umAWteE2cRR&pr1=33&time1=8&addr2=1F1Afwxr2xrs3ZQpf6ifqfNMxJWZt2JupK&pr2=67&time2=16&bitcode=AcOw&fee=0.6523";

			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

					Assert.Equal(content, "error=Invalid Bitcode");
				}
			}
		}
	}
#pragma warning restore CS0618 // Type or member is obsolete
}