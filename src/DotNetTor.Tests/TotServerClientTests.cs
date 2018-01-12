using DotNetEssentials.Logging;
using DotNetTor.Exceptions;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DotNetTor.Tests
{
	public class TotServerClientTests : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public TotServerClientTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
		}

		[Fact]
		public async Task BlockingPingTestAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5280);
			var server = new TotServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			try
			{
				await server.StartAsync();

				for (int i = 0; i < 3; i++)
				{
					using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
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
		public async Task AutoReconnectsTestAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5280);
			var server = new TotServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			try
			{
				await server.StartAsync();

				using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					await client.PingAsync();
				}

				await server.StopAsync();

				await server.StartAsync();

				using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					await client.PingAsync();

					await server.StopAsync();

					await server.StartAsync();

					await client.PingAsync();

					await server.StopAsync();

					await Assert.ThrowsAsync<ConnectionException>(async () => await client.PingAsync());

					await server.StartAsync();

					await client.PingAsync();
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
			var server = new TotServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			var clients = new List<TotClient>();
			try
			{
				await server.StartAsync();

				var connectionTasks = new List<Task<TotClient>>();

				for (int i = 0; i < 100; i++)
				{
					connectionTasks.Add(manager.EstablishTotConnectionAsync(serverEndPoint));
				}
				var pingTasks = new List<Task>();
				foreach (var cTask in connectionTasks)
				{
					TotClient client = await cTask;
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
			var server = new TotServer(serverEndPoint);
			server.RequestArrived += Server_RequestArrivedAsync;
			var manager = new TorSocks5Manager(null);
			try
			{
				await server.StartAsync();

				using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					var response = await client.RequestAsync(new TotRequest("hello"));
					Assert.Equal("world", response.ToString());
					response = await client.RequestAsync(new TotRequest("hello"));
					Assert.Equal("world", response.ToString());
					await Assert.ThrowsAsync<TotRequestException>(async () => await client.RequestAsync(new TotRequest("hell")));
					var r1 = new TotRequest("hello");
					var bytes = r1.ToBytes();
					bytes[0] = 2; // change the version to 2
					var r2 = new TotRequest();
					r2.FromBytes(bytes);
					var thrownVersionMismatchException = false;
					try
					{
						await client.RequestAsync(r2);
					}
					catch(Exception ex)
					{
						thrownVersionMismatchException = true;
						Assert.Equal("Server responded with wrong version. Expected: X'02'. Actual: X'01'.", ex.Message);
					}
					Assert.True(thrownVersionMismatchException);
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
			var client = sender as TotClient;
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
			catch (Exception ex)
			{
				Logger.LogTrace<TotServerClientTests>(ex);
				var response = TotResponse.BadRequest;
				await client.RespondAsync(response);
			}
		}

		[Fact]
		public async Task CanSubscribeAfterSubscribedAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5283);
			var server = new TotServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);

			try
			{
				await server.StartAsync();
				server.RegisterSubscription("foo");
				server.RegisterSubscription("bar");
				server.RegisterSubscription("buz");

				using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					await Assert.ThrowsAsync<TotRequestException>(async () => await client.SubscribeAsync("foo2"));
					await client.SubscribeAsync("foo");

					await server.PingAllSubscribersAsync();

					await client.SubscribeAsync("bar");
					await client.SubscribeAsync("buz");
				}
			}
			finally
			{
				await server.StopAsync();
			}
		}

		[Fact]
		public async Task ChannelIsolationAsync()
		{
			// The server and client MUST either communicate through a RequestResponse channel or a SubscribeNotify channel. 
			// A client and the server MAY have multiple channels open through different TCP connections. 
			// If these TCP connection were opened through Tor's SOCKS5 proxy with stream isolation, 
			// it can be used in a way, that the server does not learn the channels are originated from the same client.
			// The nature of the channel is defined by the first request of the client. 
			// If it is a Request, then the channel is a RequestResponse channel, if it is a SubscribeRequest, 
			// then the channel is a SubscribeNotify channel.
			// For a Request to a SubscribeNotify channel the server MUST respond with BadRequest, 
			// where the Content is: Cannot send Request to a SubscribeNotify channel.
			// For a SubscribeRequest to a RequestResponse channel the server MUST respond with BadRequest, where the Content is: 
			// Cannot send SubscribeRequest to a RequestResponse channel.

			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5283);
			var server = new TotServer(serverEndPoint);
			server.RequestArrived += Server_RequestArrivedAsync;
			var manager = new TorSocks5Manager(null);
			
			try
			{
				await server.StartAsync();
				server.RegisterSubscription("foo");

				using (TotClient requesterClient = await manager.EstablishTotConnectionAsync(serverEndPoint))
				using (TotClient subscriberClient = await manager.EstablishTotConnectionAsync(serverEndPoint))
				using (TotClient badRequesterClient = await manager.EstablishTotConnectionAsync(serverEndPoint))
				using (TotClient badSubscriberClient = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					var responseContent = await requesterClient.RequestAsync(new TotRequest("hello"));
					string worldString = responseContent.ToString();
					Assert.Equal("world", worldString);					
					await Assert.ThrowsAsync<InvalidOperationException>(async () => await requesterClient.SubscribeAsync("foo"));
					
					await subscriberClient.SubscribeAsync("foo");
					await Assert.ThrowsAsync<InvalidOperationException>(async () => await subscriberClient.RequestAsync(new TotRequest("hello")));
					
					await Assert.ThrowsAsync<TotRequestException>(async () => await badRequesterClient.RequestAsync(new TotRequest("hello2")));
					await Assert.ThrowsAsync<InvalidOperationException>(async () => await badRequesterClient.SubscribeAsync("foo"));
					responseContent = await badRequesterClient.RequestAsync(new TotRequest("hello"));
					worldString = responseContent.ToString();
					Assert.Equal("world", worldString);

					await Assert.ThrowsAsync<TotRequestException>(async () => await badSubscriberClient.SubscribeAsync("foo2"));
					await Assert.ThrowsAsync<InvalidOperationException>(async () => await badSubscriberClient.RequestAsync(new TotRequest("hello")));
					await badSubscriberClient.SubscribeAsync("foo");
				}
			}
			finally
			{
				server.RequestArrived -= Server_RequestArrivedAsync;
				await server.StopAsync();
			}
		}

		private const int NotificationReceivingClientCount = 100;

		[Fact]
		public async Task SubsctiptionTestsAsync()
		{
			var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5283);
			var server = new TotServer(serverEndPoint);
			var manager = new TorSocks5Manager(null);
			var clients = new List<TotClient>();
			try
			{
				await server.StartAsync();

				using (TotClient client = await manager.EstablishTotConnectionAsync(serverEndPoint))
				{
					await Assert.ThrowsAsync<TotRequestException>(async () => await client.SubscribeAsync("foo"));
					Assert.Empty(server.Subscriptions);
					server.RegisterSubscription("foo");
					Assert.Single(server.Subscriptions);
					Assert.Empty(server.Subscriptions.Single().Value);
					await client.SubscribeAsync("foo");
					Assert.Single(server.Subscriptions);
					Assert.Single(server.Subscriptions.Single().Value);
					await server.NotifyAllSubscribersAsync(new TotNotification("foo", new TotContent("bar")));
					Assert.Single(server.Subscriptions.Single().Value);
				}
				await Task.Delay(1000); // make sure the server already remove the client from the subscribers
				Assert.Single(server.Subscriptions);

				var connectionTasks = new List<Task<TotClient>>();

				for (int i = 0; i < NotificationReceivingClientCount; i++)
				{
					connectionTasks.Add(manager.EstablishTotConnectionAsync(serverEndPoint));
				}
				var subscriptionJobs = new List<Task>();
				foreach (var cTask in connectionTasks)
				{
					TotClient client = await cTask;
					clients.Add(client);
					client.NotificationArrived += Client_FooBarNotificationArrived;
					subscriptionJobs.Add(client.SubscribeAsync("foo"));
				}

				await Assert.ThrowsAsync<TotRequestException>(async () => await clients.First().SubscribeAsync("moo"));

				await Task.WhenAll(subscriptionJobs);

				await Assert.ThrowsAsync<TotRequestException>(async () => await clients.First().SubscribeAsync("moo"));
				server.RegisterSubscription("moo");
				await clients.First().SubscribeAsync("moo");
				server.RegisterSubscription("boo");
				server.RegisterSubscription("loo");
				await clients.First().SubscribeAsync("boo");
				await clients.First().SubscribeAsync("loo");

				Assert.Equal(NotificationReceivingClientCount, server.Subscriptions.Single(x => x.Key == "foo").Value.Count());

				await server.NotifyAllSubscribersAsync(new TotNotification("foo", new TotContent("bar")));
				await Task.Delay(1000); // wait until all notification received
				Assert.Equal(NotificationReceivingClientCount, Interlocked.Read(ref _notificationReceivedCount));
			}
			finally
			{
				await server.StopAsync();
				foreach (var client in clients)
				{
					client.NotificationArrived -= Client_FooBarNotificationArrived;
					client?.Dispose();
				}
			}
		}

		private long _notificationReceivedCount = 0;
		private void Client_FooBarNotificationArrived(object sender, TotNotification e)
		{
			Assert.Equal("foo", e.Purpose.ToString());
			Assert.Equal("bar", e.Content.ToString());
			Interlocked.Increment(ref _notificationReceivedCount);
		}
	}
}
