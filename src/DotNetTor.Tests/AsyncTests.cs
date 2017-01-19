using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using DotNetTor.SocksPort.Net;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class AsyncTests
	{
		[Fact]
		public async Task CanDoRequest1Async()
		{
			var contents = await QBitTestAsync(1).ConfigureAwait(false);
			foreach (var content in contents)
			{
				Assert.Equal(content, "\"Good question Holmes !\"");
			}
		}
		[Fact]
		public async Task CanDoRequest2Async()
		{
			var contents = await QBitTestAsync(2).ConfigureAwait(false);
			foreach (var content in contents)
			{
				Assert.Equal(content, "\"Good question Holmes !\"");
			}
		}
		[Fact]
		public async Task CanDoRequestManyAsync()
		{
			var contents = await QBitTestAsync(30).ConfigureAwait(false);
			foreach (var content in contents)
			{
				Assert.Equal(content, "\"Good question Holmes !\"");
			}
		}
		private static async Task<List<string>> QBitTestAsync(int times)
		{
			var requestUri = "http://api.qbit.ninja/whatisit/what%20is%20my%20future";
			using (var handler = new SocksPortHandler(Shared.HostAddress, Shared.SocksPort))
			using (var httpClient = new HttpClient(handler))
			{
				var tasks = new List<Task<HttpResponseMessage>>();
				for (var i = 0; i < times; i++)
				{
					var task = httpClient.GetAsync(requestUri);
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
}
