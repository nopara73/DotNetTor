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
		
		public ConcurrentHashSet<TcpClient> Clients { get; }
		public ConcurrentHashSet<Task> ClientListeners { get; }

		private Task AcceptTcpConnectionsTask { get; set; }
		private CancellationTokenSource StopAcceptingTcpConnections { get; set; }

		#endregion

		#region ConstructorsAndInitializers

		public TorOverTcpServer(IPEndPoint bindToEndPoint)
		{
			Guard.NotNull(nameof(bindToEndPoint), bindToEndPoint);
			TcpListener = new TcpListener(bindToEndPoint);
			Clients = new ConcurrentHashSet<TcpClient>();
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
					TcpClient client = await TcpListener.AcceptTcpClientAsync().WithAwaitCancellationAsync(cancel).ConfigureAwait(false);
					Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
					Console.WriteLine($"Number of clients: {Clients.Count}");
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

		private async Task ListenClientAsync(TcpClient client, CancellationToken cancel)
		{
			Guard.NotNull(nameof(client), client);
			Guard.NotNull(nameof(cancel), cancel);

			try
			{
				var stream = client.GetStream();
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
						break;
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
			catch(OperationCanceledException)
			{
				RemoveClient(client);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				RemoveClient(client);
			}
		}

		private async Task HandleRequestAsync(TcpClient client, byte[] bytes)
		{
			Guard.NotNull(nameof(client), client);
			Guard.NotNullOrEmpty(nameof(bytes), bytes);
			Guard.InRangeAndNotNull($"{nameof(bytes)}.{nameof(bytes.Length)}", bytes.Length, 7, 536870912 + 3 + 4 + 255);

			var stream = client.GetStream();

			var messageType = new TotMessageType();
			messageType.FromByte(bytes[1]);

			if(messageType == TotMessageType.Ping)
			{
				var request = new TotPing();
				request.FromBytes(bytes);

				var response = TotPong.Instance;
				var responseBytes = response.ToBytes();
				await stream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);
			}
			else if (messageType == TotMessageType.Request)
			{
				var request = new TotRequest();
				request.FromBytes(bytes);
				OnRequestArrived(request);
			}
			else if (messageType == TotMessageType.SubscribeRequest)
			{
				var request = new TotSubscribeRequest();
				request.FromBytes(bytes);
				OnSubscribeRequestArrived(request);
			}
			else if (messageType == TotMessageType.UnsubscribeRequest)
			{
				var request = new TotUnsubscribeRequest();
				request.FromBytes(bytes);
				OnUnsubscribeRequestArrived(request);
			}
			else
			{
				var notSupportedMessageType = new TotResponse(TotPurpose.BadRequest, new TotContent($"Message type is not supported. Value: {messageType}."));
				var notSupportedMessageTypeBytes = TotResponse.BadRequest.ToBytes();
				await stream.WriteAsync(notSupportedMessageTypeBytes, 0, notSupportedMessageTypeBytes.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);
			}
		}

		#endregion

		#region Events

		public event EventHandler<TotRequest> RequestArrived;
		public void OnRequestArrived(TotRequest request) => RequestArrived?.Invoke(this, request);

		public event EventHandler<TotSubscribeRequest> SubscribeRequestArrived;
		public void OnSubscribeRequestArrived(TotSubscribeRequest request) => SubscribeRequestArrived?.Invoke(this, request);

		public event EventHandler<TotUnsubscribeRequest> UnsubscribeRequestArrived;
		public void OnUnsubscribeRequestArrived(TotUnsubscribeRequest request) => UnsubscribeRequestArrived?.Invoke(this, request);

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

		private void RemoveClient(TcpClient client)
		{
			Clients.TryRemove(client);
			DisposeTcpClient(client);
		}

		private static void DisposeTcpClient(TcpClient client)
		{
			try
			{
				if (client != null)
				{
					if (client.Connected)
					{
						client.GetStream().Dispose();
					}
					client?.Dispose();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			client?.Dispose();
		}

		#endregion
	}
}
