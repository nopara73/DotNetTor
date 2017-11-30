using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;
using System.Text.RegularExpressions;
using System.Diagnostics;
using QBitNinja.Client;
using NBitcoin;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class AsyncTests
	{
		[Fact]
		public async Task CanDoRequest1Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 1).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal("\"Good question Holmes !\"", content);
				}
			}
		}
		[Fact]
		public async Task CanRequestChunkEncodedAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var response = await client.GetAsync("https://jigsaw.w3.org/HTTP/ChunkedScript").ConfigureAwait(false);
				var content = await response.Content.ReadAsStringAsync();
				Assert.Equal(1000, Regex.Matches(content, "01234567890123456789012345678901234567890123456789012345678901234567890").Count);
			}
		}
		[Fact]
		public async Task TestMicrosoftNCSI()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var response = await client.GetAsync("http://www.msftncsi.com/ncsi.txt").ConfigureAwait(false);
				var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				Assert.Equal("Microsoft NCSI", content);
			}
		}
		[Fact]
		public async Task CanRequestGzipEncoded()
		{
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			{
				var client = new QBitNinjaClient(Network.Main);
				client.SetHttpMessageHandler(handler);

				var response = await client.GetBlock(new QBitNinja.Client.Models.BlockFeature(new uint256("0000000000000000004e24d06073aef7a5313d4ea83a5c105b3cadd0d38cc1f0")), true).ConfigureAwait(false);

				Assert.Equal(474010, response.AdditionalInformation.Height);
				Assert.Null(response.Block);
				Assert.Null(response.ExtendedInformation);
			}
		}
		[Fact]
		public async Task CanDoRequest2Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 2).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal("\"Good question Holmes !\"", content);
				}
			}
		}
		[Fact]
		public async Task CanDoRequestManyAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 15).ConfigureAwait(false);
				foreach (var content in contents)
				{
					Assert.Equal("\"Good question Holmes !\"", content);
				}
			}
		}
		[Fact]
		public async Task CanDoRequestManyDifferentAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				await QBitTestAsync(client, 10, alterRequests: true).ConfigureAwait(false);
			}
		}
		[Fact]
		public async Task CanDoHttpsRequestManyAsync()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
			using (var client = new HttpClient(handler))
			{
				var contents = await QBitTestAsync(client, 15, https: true).ConfigureAwait(false);

				foreach (var content in contents)
				{
					Assert.Equal("\"Good question Holmes !\"", content);
				}
			}
		}
		[Fact]
		public async Task CanDoHttpsRequest1Async()
		{
			var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort);
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
