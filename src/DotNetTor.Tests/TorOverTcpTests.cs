using DotNetTor.Exceptions;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DotNetTor.Tests
{
	public class TorOverTcpTests
	{
		[Fact]
		public async Task BlockingPingTestAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5280);
			var server = new TorOverTcpServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			try
			{
				server.Start();

				for (int i = 0; i < 3; i++)
				{
					using (TorOverTcpClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
					{
						await client.PingAsync();
					}
				}
			}
			finally
			{
				await server.StopAsync();
			}
		}

		[Fact]
		public async Task NonBlockingPingTestAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5281);
			var server = new TorOverTcpServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			var clients = new List<TorOverTcpClient>();
			try
			{
				server.Start();

				var connectionTasks = new List<Task<TorOverTcpClient>>();

				for (int i = 0; i < 100; i++)
				{
					connectionTasks.Add(manager.EstablishTotConnectionAsync(serverEndPoint));
				}
				var pingTasks = new List<Task>();
				foreach (var cTask in connectionTasks)
				{
					TorOverTcpClient client = await cTask;
					clients.Add(client);
					pingTasks.Add(client.PingAsync());
					pingTasks.Add(client.PingAsync());
					pingTasks.Add(client.PingAsync());
				}
				await Task.WhenAll(pingTasks);
			}
			finally
			{
				await server.StopAsync();
				foreach(var client in clients)
				{
					client?.Dispose();
				}
			}
		}

		[Fact]
		public async Task RespondsAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5282);
			var server = new TorOverTcpServer(serverEndPoint);
			server.RequestArrived += Server_RequestArrivedAsync;
			var manager = new TorSocks5Manager(null);
			try
			{
				server.Start();


				using (TorOverTcpClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					var response = await client.RequestAsync(new TotRequest("hello"));
					Assert.Equal("world", response.ToString());
					response = await client.RequestAsync(new TotRequest("hello"));
					Assert.Equal("world", response.ToString());
					await Assert.ThrowsAsync<TotRequestException>(async () => await client.RequestAsync(new TotRequest("hell")));
				}
			}
			finally
			{
				server.RequestArrived -= Server_RequestArrivedAsync;
				await server.StopAsync();
			}
		}

		private async void Server_RequestArrivedAsync(object sender, TotRequest request)
		{
			var client = sender as TorOverTcpClient;
			try
			{
				if (request.Purpose.ToString() == "hello")
				{
					var response = new TotResponse(TotPurpose.Success, new TotContent("world"));
					await client.RespondAsync(response);
				}
				else
				{
					var response = TotResponse.BadRequest;
					await client.RespondAsync(response);
				}
			}
			catch (Exception)
			{
				var response = TotResponse.BadRequest;
				await client.RespondAsync(response);
			}
		}
	}
}
