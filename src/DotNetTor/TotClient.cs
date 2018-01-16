using ConcurrentCollections;
using DotNetEssentials;
using DotNetEssentials.Logging;
using DotNetTor.Exceptions;
using DotNetTor.TorOverTcp.Models;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
	/// <summary>
	/// Create an instance with the TorSocks5Manager
	/// </summary>
	public class TotClient
    {
		#region PropertiesAndMembers

		public TorSocks5Client TorSocks5Client { get; private set; }

		public TotChannelType Channel;
		
		private Task NotificationListenerTask { get; set; }
		private CancellationTokenSource StopListeningForNotifications { get; set; }

		public ConcurrentHashSet<string> Subscriptions { get; }

		public volatile bool RequestInProcess;

		#endregion

		#region Events

		public event EventHandler<TotNotification> NotificationArrived;
		public void OnNotificationArrived(TotNotification notification) => NotificationArrived?.Invoke(this, notification);

		#endregion

		#region ConstructorsAndInitializers

		internal TotClient(TorSocks5Client torSocks5Client)
		{
			TorSocks5Client = Guard.NotNull(nameof(torSocks5Client), torSocks5Client);
			Channel = TotChannelType.Undefined;
			NotificationListenerTask = null;
			StopListeningForNotifications = new CancellationTokenSource();
			RequestInProcess = false;
		}

		#endregion

		#region ServerMethods

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task RespondAsync(TotResponse response)
		{
			Guard.NotNull(nameof(response), response);

			var stream = TorSocks5Client.Stream;

			var responseBytes = response.ToBytes();

			// don't lock it, it's called from a lock!
			while(RequestInProcess)
			{
				await Task.Delay(10).ConfigureAwait(false);
			}
			RequestInProcess = true;
			try
			{
				await stream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);
			}
			finally
			{
				RequestInProcess = false;
			}
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task NotifyAsync(TotNotification notification)
		{
			Guard.NotNull(nameof(notification), notification);

			AssertOrSetChannel(TotChannelType.SubscribeNotify);

			var stream = TorSocks5Client.Stream;

			var responseBytes = notification.ToBytes();

			await DelayUnillRequestInProcessAsync().ConfigureAwait(false);
			try
			{
				await stream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);
			}
			finally
			{
				RequestInProcess = false;
			}
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task PongAsync()
		{
			var stream = TorSocks5Client.Stream;

			var responseBytes = TotPong.Instance.ToBytes();

			await DelayUnillRequestInProcessAsync().ConfigureAwait(false);
			try
			{
				// don't lock it, it's called from a lock!
				await stream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);
			}
			finally
			{
				RequestInProcess = false;
			}
		}

		#endregion

		#region ClientMethods

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task<TotContent> RequestAsync(TotRequest request)
		{
			Guard.NotNull(nameof(request), request);

			AssertOrSetChannel(TotChannelType.RequestResponse);

			byte[] responseBytes = null;
			await DelayUnillRequestInProcessAsync().ConfigureAwait(false);
			try
			{
				responseBytes = await TorSocks5Client.SendAsync(request.ToBytes());
			}
			finally
			{
				RequestInProcess = false;
			}

			var response = new TotResponse();
			response.FromBytes(responseBytes);

			AssertVersion(request.Version, response.Version);
			AssertSuccess(response);

			return response.Content;
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task SubscribeAsync(string purpose)
		{
			purpose = Guard.NotNullOrEmptyOrWhitespace(nameof(purpose), purpose, trim: true);

			AssertOrSetChannel(TotChannelType.SubscribeNotify);

			var request = new TotSubscribeRequest(purpose);
			
			await DelayUnillRequestInProcessAsync().ConfigureAwait(false);
			byte[] responseBytes = null;
			try
			{
				responseBytes = await TorSocks5Client.SendAsync(request.ToBytes());
			}
			finally
			{
				RequestInProcess = false;
			}

			var response = new TotResponse();
			response.FromBytes(responseBytes);

			AssertVersion(request.Version, response.Version);
			AssertSuccess(response);

			if (NotificationListenerTask == null)
			{
				StopListeningForNotifications?.Dispose();
				StopListeningForNotifications = new CancellationTokenSource();
				NotificationListenerTask = ListenNotificationsAsync(StopListeningForNotifications.Token);
			}
		}

		private async Task ListenNotificationsAsync(CancellationToken cancel)
		{
			Guard.NotNull(nameof(cancel), cancel);

			try
			{
				var stream = TorSocks5Client.TcpClient.GetStream();
				var receiveBufferSize = 2048;
				// Receive the response
				var receiveBuffer = new byte[receiveBufferSize];

				while (true)
				{
					cancel.ThrowIfCancellationRequested();

					while (!stream.DataAvailable || RequestInProcess)
					{
						await Task.Delay(100, cancel).ConfigureAwait(false);
					}
					using (await TorSocks5Client.AsyncLock.LockAsync().ConfigureAwait(false))
					{
						int receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBufferSize, cancel).ConfigureAwait(false);
											
						if (receiveCount <= 0)
						{
							throw new ConnectionException($"Disconnected from the server: {TorSocks5Client.TcpClient.Client.RemoteEndPoint}.");
						}
						// if we could fit everything into our buffer, then return it
						if (!stream.DataAvailable)
						{
							await HandleNotificationAsync(receiveBuffer.Take(receiveCount).ToArray());
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
								throw new ConnectionException($"Disconnected from the server: {TorSocks5Client.TcpClient.Client.RemoteEndPoint}.");
							}
							builder.Append(receiveBuffer.Take(receiveCount).ToArray());
						}

						await HandleNotificationAsync(builder.ToArray());
					}

					Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
				}
			}
			catch (OperationCanceledException ex)
			{
				Logger.LogTrace<TotClient>(ex);
			}
			catch (Exception ex)
			{
				throw new ConnectionException($"Disconnected from the server: {TorSocks5Client.TcpClient.Client.RemoteEndPoint}.", ex);
			}
		}

		private async Task HandleNotificationAsync(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			try
			{
				var messageType = new TotMessageType();
				messageType.FromByte(bytes[1]);

				if (messageType == TotMessageType.Ping)
				{
					var request = new TotPing();
					request.FromBytes(bytes);
										
					await PongAsync().ConfigureAwait(false);
					return;
				}
				else if (messageType != TotMessageType.Notification)
				{
					throw new InvalidOperationException($"Only {nameof(TotMessageType.Notification)} is accepted in a {Channel} channel. Type of received message: {nameof(messageType)}.");
				}

				var notification = new TotNotification();
				notification.FromBytes(bytes);
				OnNotificationArrived(notification);
			}
			catch (Exception ex)
			{
				// swallow and log and listen for more notification
				Logger.LogWarning<TotClient>(ex, LogLevel.Debug);
			}
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task PingAsync()
		{
			var ping = TotPing.Instance;

			byte[] responseBytes = null;
			await DelayUnillRequestInProcessAsync().ConfigureAwait(false);
			try
			{
				responseBytes = await TorSocks5Client.SendAsync(ping.ToBytes());
			}
			finally
			{
				RequestInProcess = false;
			}
			var pong = new TotPong();
			pong.FromBytes(responseBytes);

			AssertVersion(ping.Version, pong.Version);
		}

		#endregion

		#region CommonMethods

		private void AssertOrSetChannel(TotChannelType channel)
		{
			if(Channel == TotChannelType.Undefined)
			{
				Channel = channel;
				return;
			}

			if(Channel != channel)
			{
				throw new InvalidOperationException($"Wrong {nameof(TotChannelType)}. This TCP connection can only handle {Channel} operations. Requested: {channel} operation.");
			}
		}

		private async Task DelayUnillRequestInProcessAsync()
		{
			while (RequestInProcess)
			{
				await Task.Delay(10).ConfigureAwait(false);
			}
			RequestInProcess = true;
		}

		private static void AssertVersion(TotVersion expected, TotVersion actual)
		{
			if (expected != actual)
			{
				throw new TotRequestException($"Server responded with wrong version. Expected: {expected}. Actual: {actual}.");
			}
		}

		private static void AssertSuccess(TotResponse response)
		{
			if (response.Purpose != TotPurpose.Success)
			{
				string errorMessage = $"Server responded with {response.Purpose}.";
				if (response.Content != TotContent.Empty)
				{
					errorMessage += $" Details: {response.Content}.";
				}
				throw new TotRequestException(errorMessage);
			}
		}

		#endregion

		#region IDisposable Support

		public async Task DisposeAsync()
		{
			try
			{
				StopListeningForNotifications?.Cancel();
				if(NotificationArrived != null)
				{
					await NotificationListenerTask.ConfigureAwait(false);
				}
				StopListeningForNotifications?.Dispose();
			}
			catch(Exception ex)
			{
				Logger.LogWarning<TotClient>(ex, LogLevel.Debug);
			}

			TorSocks5Client?.Dispose();
		}

		#endregion
	}
}
