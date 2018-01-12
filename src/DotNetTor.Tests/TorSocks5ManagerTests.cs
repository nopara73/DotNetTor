using DotNetTor.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DotNetTor.Tests
{
	public class TorSocks5ManagerTests : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public TorSocks5ManagerTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
		}

		[Fact]
		public async Task IsolatesStreamsAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			var clearnetManager = new TorSocks5Manager(null);
			var clients = new HashSet<TorSocks5Client>();
			try
			{
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: true));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: true));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: true));
				clients.Add(await clearnetManager.EstablishTcpConnectionAsync("api.ipify.org", 80));


				var ips = new HashSet<string>();
				foreach (var client in clients)
				{
					var sendBuff = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost:api.ipify.org\r\n\r\n");
					byte[] response = await client.SendAsync(sendBuff);
					ips.Add(Encoding.ASCII.GetString(response).Split("\n").Last());
				}

				Assert.True(ips.Count >= 3);
			}
			finally
			{
				foreach (var client in clients)
				{
					client?.Dispose();
				}
			}
		}

		[Fact]
		public async Task IsolatesStreamsByIdentityAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			var clients = new HashSet<TorSocks5Client>();
			try
			{
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, "alice"));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, "bob"));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, "alice"));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, "bob"));


				var ips = new HashSet<string>();
				foreach (var client in clients)
				{
					var sendBuff = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost:api.ipify.org\r\n\r\n");
					byte[] response = await client.SendAsync(sendBuff);
					ips.Add(Encoding.ASCII.GetString(response).Split("\n").Last());
				}

				Assert.True(ips.Count >= 2);
			}
			finally
			{
				foreach (var client in clients)
				{
					client?.Dispose();
				}
			}
		}

		[Fact]
		public async Task DoesntIsolateStreamsAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			var clients = new HashSet<TorSocks5Client>();
			try
			{
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: false));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: false));
				clients.Add(await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: false));


				var ips = new HashSet<IPAddress>();
				foreach (var client in clients)
				{
					var sendBuff = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost:api.ipify.org\r\n\r\n");
					byte[] response = await client.SendAsync(sendBuff);
					string ipString = Encoding.ASCII.GetString(response).Split("\n").Last();
					ips.Add(IPAddress.Parse(ipString));
				}

				Assert.True(ips.Count < 3);
			}
			finally
			{
				foreach (var client in clients)
				{
					client?.Dispose();
				}
			}
		}

		[Fact]
		public async Task ReceiveBufferProperlySetAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			using (var client = await manager.EstablishTcpConnectionAsync("api.ipify.org", 80, isolateStream: false))
			{
				var sendBuff = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost:api.ipify.org\r\n\r\n");
				byte[] response = await client.SendAsync(sendBuff);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, null);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, -1);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, 0);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, 1);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, 2);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, 21);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, int.MinValue);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());

				response = await client.SendAsync(sendBuff, int.MaxValue);
				IPAddress.Parse(Encoding.ASCII.GetString(response).Split("\n").Last());
			}
		}

		[Fact]
		public async Task CanConnectDomainAndIpAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);

			TorSocks5Client c1 = null;
			TorSocks5Client c2 = null;
			try
			{
				c1 = await manager.EstablishTcpConnectionAsync(new IPEndPoint(IPAddress.Parse("192.64.147.228"), 80));
				c2 = await manager.EstablishTcpConnectionAsync("google.com", 443);
				c2 = await manager.EstablishTcpConnectionAsync("facebookcorewwwi.onion", 443);
			}
			finally
			{
				c1?.Dispose();
				c2?.Dispose();
			}
		}

		[Fact]
		public async Task CanResolveAsync()
		{
			var torManager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			var t1 = await torManager.ReverseResolveAsync(IPAddress.Parse("192.64.147.228"), false);
			var t2 = await torManager.ResolveAsync("google.com", false);

			var clearnetManager = new TorSocks5Manager(null);
			var c1 = await clearnetManager.ReverseResolveAsync(IPAddress.Parse("192.64.147.228"), false);
			var c2 = await clearnetManager.ResolveAsync("google.com", false);

			Assert.Equal(c1, t1);
		}

		[Fact]
		public async Task ThrowsProperExceptionsAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			await Assert.ThrowsAsync<TorSocks5FailureResponseException>(async () 
				=> await manager.ReverseResolveAsync(IPAddress.Parse("0.64.147.228"), isolateStream: false));
			await Assert.ThrowsAsync<TorSocks5FailureResponseException>(async ()
				=>
			{
				TorSocks5Client c1 = null;
				try
				{
					c1 = await manager.EstablishTcpConnectionAsync(new IPEndPoint(IPAddress.Parse("192.64.147.228"), 302), false);
				}
				finally
				{
					c1?.Dispose();
				}
			});
		}

		[Fact]
		public async Task CanAsyncronouslyConnectAndSendDataAndResolveAsync()
		{
			var manager = new TorSocks5Manager(SharedFixture.TorSock5EndPoint);
			var connectionTasks = new List<Task<TorSocks5Client>>();
			try
			{
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("api.ipify.org", 80));
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("bitcoin.org", 80));
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("api.ipify.org", 80));
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("api.ipify.org", 80));
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("pets.com", 80));
				connectionTasks.Add(manager.EstablishTcpConnectionAsync("google.com", 443, true));

				var t1 = manager.ReverseResolveAsync(IPAddress.Parse("192.64.147.228"), false);
				var t2 = manager.ResolveAsync("google.com", false);
				
				var ipTasks = new HashSet<Task<byte[]>>();
				var sendBuff = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost:api.ipify.org\r\n\r\n");
				for (int i = 0; i < connectionTasks.Count; i++)
				{
					if(i == 0 || i == 2 || i == 3)
					{
						var c = await connectionTasks[i];
						ipTasks.Add(c.SendAsync(sendBuff));
					}
				}

				var bitcoinClient = await connectionTasks[1];
				var bitcoinErrorResponse = await bitcoinClient.SendAsync(sendBuff);
				string bitcoinErrorResponseString = Encoding.ASCII.GetString(bitcoinErrorResponse);
				Assert.Contains("moved permanently", bitcoinErrorResponseString, StringComparison.OrdinalIgnoreCase);


				foreach (var ipTask in ipTasks)
				{
					byte[] response = await ipTask;
					string responseString = Encoding.ASCII.GetString(response);
					IPAddress.Parse(responseString.Split("\n").Last());
				}

				await Task.WhenAll(t1, t2);
				Assert.NotNull(await t1);
				Assert.NotNull(await t2);
			}
			finally
			{
				foreach(var task in connectionTasks)
				{
					var client = await task;
					client?.Dispose();
				}
			}
		}
	}
}
