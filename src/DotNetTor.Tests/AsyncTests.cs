using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class AsyncTests
	{
		[Fact]
		public async Task CanDoRequest1Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 1).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}
		[Fact]
		public async Task CanRequestChunkEncodedAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var response = await client.GetAsync("https://jigsaw.w3.org/HTTP/ChunkedScript").ConfigureAwait(false);
				var content = await response.Content.ReadAsStringAsync();
				Assert.Equal(1000, Regex.Matches(content, "01234567890123456789012345678901234567890123456789012345678901234567890").Count);

				var tumbleBitResponse = await client.GetAsync("http://testnet.ntumblebit.metaco.com/api/v1/tumblers/0/parameters").ConfigureAwait(false);
				var tumbleBitContent = await tumbleBitResponse.Content.ReadAsStringAsync();
				Assert.True(tumbleBitContent.Contains("TestNet"));
				Assert.True(tumbleBitContent.Contains("fakePuzzleCount"));
				Assert.True(tumbleBitContent.Contains("30820124020100300d06092a864886f70d01010105000482010e3082010a0282010100b520935292dd6ff1e6d69af2a6936bb0bb52681ec6e700b2b256cc88e80a1264c4d1390b5e37dc3540c0069680df10ffd4e16b3511264488b9f7e27eb74e4fdf97c3f18b331a2aa33541b6e2b6fbad8ebf9b2799e14af0d5b327260f162c84b16c08fdfb0730a4dac956116b6e200b33cbcdf19b270250e820c5aec8f9dcc224b5cb08f2a1a4adb583a4d70c76a252492f1b0da6e89f7d586c12f426dd1e5a9843b542eea760eb89c4a2e44cb3b1f4815866d9150b6c2c9bdf5d0e99ece9ac6df09cd13e43dc02dad19fc828f5f737b2ac9e3318ee2374bfd1b70da4884e807c6150a0ceeca20a1a62814dad7408a542f5865d7b5b3f0c1bfd8878372514dca10203010001"));
				Assert.True(tumbleBitContent.Contains("realTransactionCount"));
				Assert.True(tumbleBitContent.Contains("denomination"));
				Assert.True(tumbleBitContent.Contains("fakeFormat"));
			}
		}
		[Fact]
		public async Task TestMicrosoftNCSI()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var response = await client.GetAsync("http://www.msftncsi.com/ncsi.txt").ConfigureAwait(false);
				var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				Assert.Equal(content, "Microsoft NCSI");
			}
		}
		[Fact]
		public async Task CanDoRequest2Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 2).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}
		[Fact]
		public async Task CanDoRequestManyAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 15).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}
		[Fact]
		public async Task CanDoRequestManyDifferentAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				await QBitTestAsync(client, 10, alterRequests: true).ConfigureAwait(false);
			}
		}
		[Fact]
		public async Task CanDoHttpsRequestManyAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 15, https: true).ConfigureAwait(false);

				foreach (var content in contents)
				{
					Assert.Equal(content, "\"Good question Holmes !\"");
				}
			}
		}
		[Fact]
		public async Task CanDoHttpsRequest1Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort, ignoreSslCertification: true);
			using (var client = new HttpClient(handler))
			{
				var request = "https://api.qbit.ninja/whatisit/what%20is%20my%20future";
				var res = await client.GetAsync(request).ConfigureAwait(false);
				var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
				Assert.Equal("\"Good question Holmes !\"", content);
			}
		}

		private static async Task<List<string>> QBitTestAsync(HttpClient httpClient, int times, bool https = false, bool alterRequests = false)
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			if (https) requestUri = "https://api.qbit.ninja/whatisit/what%20is%20my%20future";

			var tasks = new List<Task<HttpResponseMessage>>();
			for (var i = 0; i < times; i++)
			{
				var task = httpClient.GetAsync(requestUri);
				if (alterRequests)
				{
					var task2 = httpClient.GetAsync("https://api.ipify.org/");
					tasks.Add(task2);
				}
				tasks.Add(task);
			}

			await Task.WhenAll(tasks).ConfigureAwait(false);

			var contents = new List<string>();
			foreach (var task in tasks)
			{
				contents.Add(await (await task.ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false));
			}

			return contents;
		}
	}
}
