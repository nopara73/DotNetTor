using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;

namespace DotNetTor.Tests
{
	// For proper configuraion see https://github.com/nopara73/DotNetTor
	public class PostTests
	{
		private static HttpClient _client;

		[Fact]
		public async Task CanDoBasicPostRequestAsync()
		{
			using (_client = new HttpClient(new SocksPortHandler(Shared.HostAddress, Shared.SocksPort)))
			{
				HttpContent content = new FormUrlEncodedContent(new[]
				{
					new KeyValuePair<string, string>("foo", "bar@98")
				});

				HttpResponseMessage message = await _client.PostAsync("http://httpbin.org/post", content).ConfigureAwait(false);
				var responseContentString = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.True(responseContentString.Contains("bar@98"));
			}
		}
		[Fact]
		public async Task CanDoBasicPostHttpsRequestAsync()
		{
			using (_client = new HttpClient(new SocksPortHandler(Shared.HostAddress, Shared.SocksPort)))
			{
				HttpContent content = new FormUrlEncodedContent(new[]
				{
					new KeyValuePair<string, string>("foo", "bar@98")
				});

				HttpResponseMessage message = await _client.PostAsync("https://httpbin.org/post", content).ConfigureAwait(false);
				var responseContentString = await message.Content.ReadAsStringAsync().ConfigureAwait(false);

				Assert.True(responseContentString.Contains("bar@98"));
			}
		}
	}
}
