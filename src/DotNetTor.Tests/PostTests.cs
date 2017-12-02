using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using Xunit;
using System.Diagnostics;
using System.Text;

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

				HttpResponseMessage message = await _client.PostAsync("http://httpbin.org/post", content);
				var responseContentString = await message.Content.ReadAsStringAsync();

				Assert.Contains("bar@98", responseContentString);
			}
		}

		[Fact]
		public async Task CanDoBasicPostRequestWithNonAsciiCharsAsync()
		{
			using (_client = new HttpClient(new SocksPortHandler(Shared.HostAddress, Shared.SocksPort)))
			{
				string json = "Hello ñ";
				var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

				HttpResponseMessage message = await _client.PostAsync("http://httpbin.org/post", httpContent);
				var responseContentString = await message.Content.ReadAsStringAsync();

				Assert.Contains(@"Hello \u00f1", responseContentString);
			}
		}

		[Fact]
		public async Task CanDoBasicPostHttpsRequestAsync()
		{
			using (_client = new HttpClient(new SocksPortHandler(Shared.HostAddress, Shared.SocksPort)))
			{
				HttpContent content = new StringContent("{\"hex\": \"01000000010000000000000000000000000000000000000000000000000000000000000000ffffffff2d03a58605204d696e656420627920416e74506f6f6c20757361311f10b53620558903d80272a70c0000724c0600ffffffff010f9e5096000000001976a9142ef12bd2ac1416406d0e132e5bc8d0b02df3861b88ac00000000\"}");

				HttpResponseMessage message = await _client.PostAsync("https://api.smartbit.com.au/v1/blockchain/decodetx", content);
				var responseContentString = await message.Content.ReadAsStringAsync();

				Debug.WriteLine(responseContentString);
				Assert.Equal("{\"success\":true,\"transaction\":{\"Version\":\"1\",\"LockTime\":\"0\",\"Vin\":[{\"TxId\":null,\"Vout\":null,\"ScriptSig\":null,\"CoinBase\":\"03a58605204d696e656420627920416e74506f6f6c20757361311f10b53620558903d80272a70c0000724c0600\",\"TxInWitness\":null,\"Sequence\":\"4294967295\"}],\"Vout\":[{\"Value\":25.21865743,\"N\":0,\"ScriptPubKey\":{\"Asm\":\"OP_DUP OP_HASH160 2ef12bd2ac1416406d0e132e5bc8d0b02df3861b OP_EQUALVERIFY OP_CHECKSIG\",\"Hex\":\"76a9142ef12bd2ac1416406d0e132e5bc8d0b02df3861b88ac\",\"ReqSigs\":1,\"Type\":\"pubkeyhash\",\"Addresses\":[\"15HCzh8AoKRnTWMtmgAsT9TKUPrQ6oh9HQ\"]}}],\"TxId\":\"a02b9bd4264ab5d7c43ee18695141452b23b230b2a8431b28bbe446bf2b2f595\"}}", responseContentString);
			}
		}
	}
}
