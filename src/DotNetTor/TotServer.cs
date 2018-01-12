using ConcurrentCollections;
using DotNetEssentials;
using DotNetEssentials.Logging;
using DotNetTor.Exceptions;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
    public class TotServer
    {
		#region PropertiesAndMembers

		public TcpListener TcpListener { get; }
		
		private HashSet<TotClient> Clients { get; set; }
		private AsyncLock ClientsAsyncLock { get; }

		private ConcurrentHashSet<Task> ClientListeners { get; set; }

		private AsyncLock RemoveClientAsyncLock { get; }

		/// <summary>
		/// string: Subscription Purpose, collection: subscribers
		/// </summary>
		public ConcurrentDictionary<string, ConcurrentHashSet<TotClient>> Subscriptions { get; private set; }

		private Task AcceptTcpConnectionsTask { get; set; }
		private CancellationTokenSource StopAcceptingTcpConnections { get; set; }

		#endregion

		#region Events

		public event EventHandler<TotRequest> RequestArrived;
		public void OnRequestArrived(TotClient client, TotRequest request) => RequestArrived?.Invoke(client, request);
		
		#endregion

		#region ConstructorsAndInitializers

		public TotServer(IPEndPoint bindToEndPoint)
		{
			Guard.NotNull(nameof(bindToEndPoint), bindToEndPoint);
			TcpListener = new TcpListener(bindToEndPoint);
			ClientsAsyncLock = new AsyncLock();
			RemoveClientAsyncLock = new AsyncLock();
		}

		public void Start()
		{
			using (ClientsAsyncLock.Lock())
			{
				if (Clients != null)
				{
					Clients.Clear();
				}
				else
				{
					Clients = new HashSet<TotClient>();
				}
			}
			ClientListeners = new ConcurrentHashSet<Task>();

			StopAcceptingTcpConnections = new CancellationTokenSource();

			Subscriptions = new ConcurrentDictionary<string, ConcurrentHashSet<TotClient>>();

			TcpListener.Start();

			// Start accepting incoming Tcp connections
			AcceptTcpConnectionsTask = AcceptTcpConnectionsAsync(StopAcceptingTcpConnections.Token);
		}

		public async Task AcceptTcpConnectionsAsync(CancellationToken cancel)
		{
			Guard.NotNull(nameof(cancel), cancel);

			while (true)
			{
				if (cancel.IsCancellationRequested) return;

				TcpClient tcpClient = null;
				TorSocks5Client ts5Client = null;
				TotClient client = null;
				try
				{
					tcpClient = await TcpListener.AcceptTcpClientAsync().WithAwaitCancellationAsync(cancel).ConfigureAwait(false);
										
					ts5Client = new TorSocks5Client(tcpClient);
					client = new TotClient(ts5Client);

					using (await ClientsAsyncLock.LockAsync())
					{
						Clients.Add(client);

						Logger.LogInfo<TotServer>($"Client connected: {tcpClient.Client.RemoteEndPoint}.\nNumber of clients: {Clients.Count}.");
					}

					// Start listening for incoming data
					Task task = ListenClientAsync(client, StopAcceptingTcpConnections.Token);
					ClientListeners.Add(task);
				}
				catch (ObjectDisposedException ex)
				{
					// This happens at TcpListener.Stop()
					Logger.LogInfo<TotServer>("Server stopped listening for TCP connections.");
					Logger.LogTrace<TotServer>(ex);
				}
				catch (OperationCanceledException ex)
				{
					// This happens when cancelling the await of AcceptTcpClientAsync()
					Logger.LogInfo<TotServer>("Server stopped listening for TCP connections.");
					Logger.LogTrace<TotServer>(ex);
				}
				catch (Exception ex)
				{
					Logger.LogWarning<TotServer>(ex, LogLevel.Debug);
					if (client != null)
					{
						await RemoveClientAsync(client).ConfigureAwait(false);
					}
					ts5Client?.Dispose();
					tcpClient?.Dispose();
				}
			}
		}


		#endregion

		#region Methods
		
		private async Task ListenClientAsync(TotClient client, CancellationToken cancel)
		{
			Guard.NotNull(nameof(client), client);
			Guard.NotNull(nameof(cancel), cancel);

			try
			{
				var stream = client.TorSocks5Client.TcpClient.GetStream();
				var receiveBufferSize = 2048;
				// Receive the response
				var receiveBuffer = new byte[receiveBufferSize];

				while (true)
				{
					cancel.ThrowIfCancellationRequested();
					
					// TODO: I don't understand why this code works (inside while block), it's just a sweetspot, where the tests are running properly for some strange reasons
					var builder = new ByteArrayBuilder();
					while (!stream.DataAvailable || client.RequestInProcess)
					{
						cancel.ThrowIfCancellationRequested();
						if (!client.RequestInProcess)
						{
							var byteBuffer = new byte[1];
							// .NET bug, cancellationtoken of the stream never actually cancels the task, leave it there
							var count = await stream.ReadAsync(byteBuffer, 0, 1).WithAwaitCancellationAsync(cancel).ConfigureAwait(false);
							if (count == 1)
							{
								builder.Append(byteBuffer);
							}
							Array.Clear(byteBuffer, 0, 1);
						}
						else
						{
							await Task.Delay(100, cancel).ConfigureAwait(false);
						}
					}

					using (await client.TorSocks5Client.AsyncLock.LockAsync().ConfigureAwait(false))
					{
						// .NET bug, cancellationtoken of the stream never actually cancels the task, leave it there
						int receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBufferSize, cancel).ConfigureAwait(false);
						if (receiveCount <= 0)
						{
							await RemoveClientAsync(client).ConfigureAwait(false);
							break;
						}
						// if we could fit everything into our buffer, then return it
						if (!stream.DataAvailable)
						{
							builder.Append(receiveBuffer.Take(receiveCount).ToArray());
							await HandleRequestAsync(client, builder.ToArray()).ConfigureAwait(false);
							Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
							continue;
						}

						// while we have data available, start building a bytearray
						builder.Append(receiveBuffer.Take(receiveCount).ToArray());
						while (stream.DataAvailable)
						{
							Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
							receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBufferSize, cancel).ConfigureAwait(false);
							if (receiveCount <= 0)
							{
								await RemoveClientAsync(client).ConfigureAwait(false);
								break;
							}
							builder.Append(receiveBuffer.Take(receiveCount).ToArray());
						}

						await HandleRequestAsync(client, builder.ToArray()).ConfigureAwait(false);
					}

					Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
				}
			}
			catch (OperationCanceledException ex)
			{
				Logger.LogTrace<TotServer>(ex);
				await RemoveClientAsync(client).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.LogTrace<TotServer>(ex);
				await RemoveClientAsync(client).ConfigureAwait(false);
			}
		}

		private async Task HandleRequestAsync(TotClient client, byte[] bytes)
		{
			Guard.NotNull(nameof(client), client);
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			try
			{
				var ver = new TotVersion();
				ver.FromByte(bytes[0]);

				if(ver != TotVersion.Version1)
				{
					await client.RespondAsync(TotResponse.VersionMismatch).ConfigureAwait(false);
					return;
				}

				var messageType = new TotMessageType();
				messageType.FromByte(bytes[1]);

				if (messageType == TotMessageType.Ping)
				{
					var request = new TotPing();
					request.FromBytes(bytes);
					
					await client.PongAsync().ConfigureAwait(false);
				}
				else if (messageType == TotMessageType.Request)
				{
					var request = new TotRequest();
					request.FromBytes(bytes);
					OnRequestArrived(client, request);
				}
				else if (messageType == TotMessageType.SubscribeRequest)
				{
					var request = new TotSubscribeRequest();
					request.FromBytes(bytes);

					var subscriptionPath = Subscriptions.FirstOrDefault(x => x.Key == request.Purpose.ToString());
					if(subscriptionPath.Key != null)
					{
						subscriptionPath.Value.Add(client);
						await client.RespondAsync(TotResponse.Success).ConfigureAwait(false);
					}
					else
					{
						await client.RespondAsync(TotResponse.BadRequest).ConfigureAwait(false);
					}
				}
				else
				{
					var notSupportedMessageTypeResponse = new TotResponse(TotPurpose.BadRequest, new TotContent($"Message type is not supported. Value: {messageType}."));
					await client.RespondAsync(notSupportedMessageTypeResponse).ConfigureAwait(false);
				}
			}
			catch(Exception ex)
			{
				Logger.LogTrace<TotServer>(ex);
				await client.RespondAsync(TotResponse.BadRequest).ConfigureAwait(false);
			}
		}

		#endregion

		#region SubscribeNotify

		public void RegisterSubscription(string subscription)
		{
			subscription = Guard.NotNullOrEmptyOrWhitespace(nameof(subscription), subscription, trim: true);

			if(!Subscriptions.TryAdd(subscription, new ConcurrentHashSet<TotClient>()))
			{
				throw new InvalidOperationException($"{nameof(subscription)} is already registered. Value: {subscription}.");
			}
		}

		public async Task PingAllSubscribersAsync()
		{
			var clientSet = new HashSet<TotClient>();
			foreach (var client in Subscriptions.Values.SelectMany(x => x))
			{
				clientSet.Add(client);
			}

			try
			{
				var tasks = new List<Task<TotClient>>();
				foreach (var client in clientSet)
				{
					tasks.Add(TryPingSubscriberAsync(client));
				}

				await Task.WhenAll(tasks);

				foreach (var task in tasks)
				{
					// It returns null if success, client if unsuccessful, so it can be disconnected.
					var client = await task.ConfigureAwait(false);
					if (client != null)
					{
						await RemoveClientAsync(client).ConfigureAwait(false);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning<TotServer>(ex, LogLevel.Debug);
			}
		}

		/// <returns>ATTENTION! It returns null if success, client if unsuccessful, so it can be disconnected.</returns>
		private async Task<TotClient> TryPingSubscriberAsync(TotClient client)
		{
			try
			{
				await client.PingAsync().ConfigureAwait(false);
				return null;
			}
			catch (Exception ex)
			{
				Logger.LogTrace<TotServer>(ex);
				return client;
			}
		}

		public async Task NotifyAllSubscribersAsync(TotNotification notification)
		{
			Guard.NotNull(nameof(notification), notification);

			var subscriptionPath = Subscriptions.FirstOrDefault(x => x.Key == notification.Purpose.ToString());
			if(subscriptionPath.Key == null)
			{
				throw new InvalidOperationException($"{nameof(notification.Purpose)} is not registered in {Subscriptions}. Value: {notification.Purpose}.");
			}

			try
			{
				var tasks = new List<Task<TotClient>>();
				foreach (var client in subscriptionPath.Value)
				{
					tasks.Add(TryNotifySubscriberAsync(client, notification));
				}

				await Task.WhenAll(tasks);

				foreach (var task in tasks)
				{
					// It returns null if success, client if unsuccessful, so it can be disconnected.
					var client = await task.ConfigureAwait(false);
					if (client != null)
					{
						await RemoveClientAsync(client).ConfigureAwait(false);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning<TotServer>(ex, LogLevel.Debug);
			}
		}

		/// <returns>ATTENTION! It returns null if success, client if unsuccessful, so it can be disconnected.</returns>
		private async Task<TotClient> TryNotifySubscriberAsync(TotClient client, TotNotification notification)
		{
			try
			{
				await client.NotifyAsync(notification).ConfigureAwait(false);
				return null;
			}
			catch (Exception ex)
			{
				Logger.LogTrace<TotServer>(ex.ToString());
				return client;
			}
		}

		#endregion

		#region Cleanup

		public async Task DisposeAsync()
		{
			try
			{
				StopAcceptingTcpConnections?.Cancel();
				if (AcceptTcpConnectionsTask != null)
				{
					await AcceptTcpConnectionsTask.ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning<TotServer>(ex, LogLevel.Debug);
			}

			if (ClientListeners != null)
			{
				foreach (var clientListener in ClientListeners)
				{
					await clientListener.ConfigureAwait(false);
				}
			}

			try
			{
				TcpListener?.Stop();
				Logger.LogInfo<TotServer>("Server stopped.");
			}
			catch (Exception ex)
			{
				Logger.LogWarning<TotServer>(ex, LogLevel.Debug);
			}

			StopAcceptingTcpConnections?.Dispose();
			StopAcceptingTcpConnections = null; // otherwise warning
		}
		
		private async Task RemoveClientAsync(TotClient client)
		{
			if (client == null) return;

			using (await RemoveClientAsyncLock.LockAsync().ConfigureAwait(false))
			{
				foreach (var subscription in Subscriptions)
				{
					subscription.Value.TryRemove(client);
				}
				using (await ClientsAsyncLock.LockAsync().ConfigureAwait(false))
				{
					Clients.Remove(client);
					var tcpClient = client.TorSocks5Client.TcpClient;
					if (tcpClient != null)
					{
						Logger.LogInfo<TotServer>($"Client removed: {tcpClient.Client.RemoteEndPoint}.\nNumber of clients: {Clients.Count}.");
					}
				}

				if (client != null)
				{
					await client.DisposeAsync().ConfigureAwait(false);
					client = null;
				}
			}
		}

		#endregion
	}
}
