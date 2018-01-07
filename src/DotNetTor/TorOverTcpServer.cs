using ConcurrentCollections;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
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
    public class TorOverTcpServer
    {
		#region PropertiesAndMembers

		public TcpListener TcpListener { get; }
		
		public ConcurrentHashSet<TorOverTcpClient> Clients { get; }
		public ConcurrentHashSet<Task> ClientListeners { get; }

		private Task AcceptTcpConnectionsTask { get; set; }
		private CancellationTokenSource StopAcceptingTcpConnections { get; set; }

		#endregion

		#region ConstructorsAndInitializers

		public TorOverTcpServer(IPEndPoint bindToEndPoint)
		{
			Guard.NotNull(nameof(bindToEndPoint), bindToEndPoint);
			TcpListener = new TcpListener(bindToEndPoint);
			Clients = new ConcurrentHashSet<TorOverTcpClient>();
			ClientListeners = new ConcurrentHashSet<Task>();
			StopAcceptingTcpConnections = new CancellationTokenSource();
		}

		#endregion

		#region Methods

		public void Start()
		{
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

				try
				{
					TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync().WithAwaitCancellationAsync(cancel).ConfigureAwait(false);
					Console.WriteLine($"Client connected: {tcpClient.Client.RemoteEndPoint}");
					Console.WriteLine($"Number of clients: {Clients.Count}");

					var ts5Client = new TorSocks5Client(tcpClient);
					var client = new TorOverTcpClient(ts5Client);
					Clients.Add(client);

					// Start listening for incoming data
					Task task = ListenClientAsync(client, StopAcceptingTcpConnections.Token);
					ClientListeners.Add(task);
				}
				catch (ObjectDisposedException)
				{
					// This happens at TcpListener.Stop()
					Console.WriteLine("Server stopped listening for TCP connections.");
				}
				catch (OperationCanceledException)
				{
					// This happens when cancelling the await of AcceptTcpClientAsync()
					Console.WriteLine("Server stopped listening for TCP connections.");
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
				}
			}
		}

		private async Task ListenClientAsync(TorOverTcpClient client, CancellationToken cancel)
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
					// .NET bug, cancellationtoken of the stream never actually cancels the task, leave it there
					int receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBufferSize, cancel).WithAwaitCancellationAsync(cancel).ConfigureAwait(false);
					if (receiveCount <= 0)
					{
						RemoveClient(client);
						break;
					}
					// if we could fit everything into our buffer, then return it
					if (!stream.DataAvailable)
					{
						await HandleRequestAsync(client, receiveBuffer.Take(receiveCount).ToArray()).ConfigureAwait(false);
						Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
						continue;
					}

					// while we have data available, start building a bytearray
					var builder = new ByteArrayBuilder();
					builder.Append(receiveBuffer.Take(receiveCount).ToArray());
					while (stream.DataAvailable)
					{
						Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
						receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBufferSize, cancel).ConfigureAwait(false);
						if (receiveCount <= 0)
						{
							RemoveClient(client);
							break;
						}
						builder.Append(receiveBuffer.Take(receiveCount).ToArray());
					}

					await HandleRequestAsync(client, builder.ToArray()).ConfigureAwait(false);

					Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
				}
			}
			catch (OperationCanceledException)
			{
				RemoveClient(client);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				RemoveClient(client);
			}
		}

		private async Task HandleRequestAsync(TorOverTcpClient client, byte[] bytes)
		{
			Guard.NotNull(nameof(client), client);
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			try
			{
				var messageType = new TotMessageType();
				messageType.FromByte(bytes[1]);

				if (messageType == TotMessageType.Ping)
				{
					var request = new TotPing();
					request.FromBytes(bytes);
					
					await client.PongAsync();
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
					OnSubscribeRequestArrived(client, request);
				}
				else if (messageType == TotMessageType.UnsubscribeRequest)
				{
					var request = new TotUnsubscribeRequest();
					request.FromBytes(bytes);
					OnUnsubscribeRequestArrived(client, request);
				}
				else
				{
					var notSupportedMessageTypeResponse = new TotResponse(TotPurpose.BadRequest, new TotContent($"Message type is not supported. Value: {messageType}."));
					await client.RespondAsync(notSupportedMessageTypeResponse);
				}
			}
			catch
			{
				await client.RespondAsync(TotResponse.BadRequest);
			}
		}

		#endregion

		#region Events

		public event EventHandler<TotRequest> RequestArrived;
		public void OnRequestArrived(TorOverTcpClient client, TotRequest request) => RequestArrived?.Invoke(client, request);

		public event EventHandler<TotSubscribeRequest> SubscribeRequestArrived;
		public void OnSubscribeRequestArrived(TorOverTcpClient client, TotSubscribeRequest request) => SubscribeRequestArrived?.Invoke(client, request);

		public event EventHandler<TotUnsubscribeRequest> UnsubscribeRequestArrived;
		public void OnUnsubscribeRequestArrived(TorOverTcpClient client, TotUnsubscribeRequest request) => UnsubscribeRequestArrived?.Invoke(client, request);

		#endregion

		#region Cleanup

		public async Task StopAsync()
		{
			try
			{
				StopAcceptingTcpConnections?.Cancel();
				await AcceptTcpConnectionsTask.ConfigureAwait(false);				
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}

			foreach (var clientListener in ClientListeners)
			{
				await clientListener.ConfigureAwait(false);
			}

			try
			{
				TcpListener?.Stop();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}

		private void RemoveClient(TorOverTcpClient client)
		{
			Clients.TryRemove(client);
			client?.Dispose();
		}

		#endregion
	}
}
