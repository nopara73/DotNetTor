using System;
using System.Net;
using System.Net.Http;
using Xunit;

namespace DotNetTor.Tests
{
	// See BasicTests.cs for proper TOR configuration
	public class ObsolateTests
    {
		[Fact]
		public void CanDoBasicRequest()
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
#pragma warning restore CS0618 // Type or member is obsolete
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
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
#pragma warning restore CS0618 // Type or member is obsolete
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
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
#pragma warning restore CS0618 // Type or member is obsolete
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
					Assert.True(gotIp);
				}

				// 2. Change TOR IP
				var ControlPortClient = new ControlPort.Client(Shared.HostAddress, Shared.ControlPort, Shared.ControlPortPassword);
#pragma warning disable CS0618 // Type or member is obsolete
				ControlPortClient.ChangeCircuit();
#pragma warning restore CS0618 // Type or member is obsolete

				// 3. Get changed TOR IP
#pragma warning disable CS0618 // Type or member is obsolete
				handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
#pragma warning restore CS0618 // Type or member is obsolete
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
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
#pragma warning restore CS0618 // Type or member is obsolete
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
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
#pragma warning restore CS0618 // Type or member is obsolete
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
			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromRequestUri(firstRequest);
#pragma warning restore CS0618 // Type or member is obsolete
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(firstRequest).Result.Content.ReadAsStringAsync().Result;
					content = httpClient.GetAsync("http://api.qbit.ninja/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true").Result.Content.ReadAsStringAsync().Result;
					content = httpClient.GetAsync("http://api.qbit.ninja/balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000").Result.Content.ReadAsStringAsync().Result;
				}
			}
		}

		[Fact]
		public void CanRequestOnion()
		{
			var requestUri = "http://bitmixer2whesjgj.onion/order.php?addr1=16HGUokcXuJXn9yiV6uQ4N3umAWteE2cRR&pr1=33&time1=8&addr2=1F1Afwxr2xrs3ZQpf6ifqfNMxJWZt2JupK&pr2=67&time2=16&bitcode=AcOw&fee=0.6523";

			using (var socksPortClient = new SocksPort.Client(Shared.HostAddress, Shared.SocksPort))
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
#pragma warning restore CS0618 // Type or member is obsolete
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;

					Assert.Equal(content, "error=Invalid Bitcode");
				}
			}
		}
	}
}
