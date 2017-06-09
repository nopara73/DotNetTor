using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.ControlPort
{
	public sealed class Client
	{
		private readonly IPEndPoint _controlEndPoint;
		private readonly string _password;
		private Socket _socket;

		public Client(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			_controlEndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			_password = password;
		}

		public async Task ChangeCircuitAsync()
		{
			try
			{
				await InitializeConnectSocketAsync().ConfigureAwait(false);

				await SendCommandAsync($"AUTHENTICATE \"{_password}\"").ConfigureAwait(false);

				// Subscribe to SIGNAL events
				await SendCommandAsync("SETEVENTS SIGNAL").ConfigureAwait(false);

				// Clear all existing circuits and build new ones
				await SendCommandAsync("SIGNAL NEWNYM").ConfigureAwait(false);

				// Unsubscribe from all events
				await SendCommandAsync("SETEVENTS").ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				throw new TorException("Couldn't change circuit", ex);
			}
			finally
			{
				DisconnectDisposeSocket();

				// safety delay, in case the tor client is not quick enough with the actions
				await Task.Delay(100).ConfigureAwait(false);
			}
		}

		private static HashSet<string> _eventsSet = new HashSet<string>();

		/// <summary>
		/// Waits for the specified event. Also consumes all responses.
		/// </summary>
		/// <param name="eventStartsWith"></param>
		/// <param name="timeout"></param>
		/// <returns>Full event</returns>
		private async Task<string> WaitForEventAsync(string eventStartsWith, TimeSpan timeout)
		{
			var timeoutTask = Task.Delay(timeout);
			while (true)
			{
				var bufferByteArraySegment = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
				Task<int> socketReceiveTask = _socket.ReceiveAsync(bufferByteArraySegment, SocketFlags.None);

				var firstTask = await Task.WhenAny(socketReceiveTask, timeoutTask).ConfigureAwait(false);

				if (firstTask == timeoutTask) throw new TimeoutException($"Did not receive the expected {nameof(eventStartsWith)} : {eventStartsWith} within the specified {nameof(timeout)} : {timeout}");

				int receivedCount = await socketReceiveTask.ConfigureAwait(false);

				var response = Encoding.ASCII.GetString(bufferByteArraySegment.Array, 0, receivedCount);
				if (response.StartsWith(eventStartsWith, StringComparison.OrdinalIgnoreCase))
				{
					return response;
				}
			}
		}

		private async Task SendCommandAsync(string command)
		{
			try
			{
				command = command.Trim();
				if (!command.EndsWith("\r\n", StringComparison.Ordinal))
				{
					command += "\r\n";
				}

				var commandByteArraySegment = new ArraySegment<byte>(Encoding.ASCII.GetBytes(command));
				await _socket.SendAsync(commandByteArraySegment, SocketFlags.None).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				throw new TorException($"Failed to send command to TOR Control Port: {nameof(command)} : {command}", ex);
			}

			var bufferByteArraySegment = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
			try
			{
				var receivedCount = await _socket.ReceiveAsync(bufferByteArraySegment, SocketFlags.None).ConfigureAwait(false);
				var response = Encoding.ASCII.GetString(bufferByteArraySegment.Array, 0, receivedCount);
				var responseLines = new List<string>(response.Split(new [] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));

				if(!responseLines.Any(x => x.StartsWith("250 OK", StringComparison.OrdinalIgnoreCase)))
					throw new TorException(
						$"Unexpected {nameof(response)} from TOR Control Port to sent {nameof(command)} : {command} , {nameof(response)} : {response}");

				// If we are tracking the signal events throw exception if didn't get the expected response
				if (command.StartsWith("SIGNAL", StringComparison.OrdinalIgnoreCase))
				{
					if (_eventsSet.Any(x => x.Equals("SIGNAL", StringComparison.OrdinalIgnoreCase)))
					{
						command = command.Replace("\r\n", "");
						var what = new List<string>(command.Split(' '))[1];
						if (!responseLines.Any(x => x.StartsWith($"650 SIGNAL {what}", StringComparison.OrdinalIgnoreCase)))
						{
							// NEWNYM may be rate-limited (usually around 10 seconds, the max I've seen is 2 minutes)
							if (what.StartsWith("NEWNYM", StringComparison.OrdinalIgnoreCase))
							{
								await WaitForEventAsync("650 SIGNAL NEWNYM", TimeSpan.FromMinutes(3)).ConfigureAwait(false);
							}
							else
							{
								throw new TorException(
									$"Didn't receive 650 SIGNAL {what} confirmation from TOR Control Port to sent {nameof(command)} : {command} , {nameof(response)} : {response}");
							}
						}
					}
				}
				// Keep track of what events we are tracking
				if (command.StartsWith("SETEVENTS", StringComparison.OrdinalIgnoreCase))
				{
					command = command.Replace("\r\n", "");
					_eventsSet = new HashSet<string>();
					var eventTypes = new List<string>(command.Split(' '));
					eventTypes.RemoveAt(0);
					foreach (string type in eventTypes) _eventsSet.Add(type);
				}
			}
			catch (TorException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new TorException(
					$"Didn't receive response for the sent {nameof(command)} from TOR Control Port: {nameof(command)} : {command}", ex);
			}
		}

		private async Task<bool> TrySendCommandAsync(string command)
		{
			try
			{
				await SendCommandAsync(command).ConfigureAwait(false);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Always use it in finally block.
		/// </summary>
		private void DisconnectDisposeSocket()
		{
			try
			{
				if (_socket.Connected)
					_socket.Shutdown(SocketShutdown.Both);
				_socket.Dispose();
			}
			catch (ObjectDisposedException)
			{
				// good, it's already disposed
			}
			catch (Exception ex)
			{
				throw new TorException("Couldn't properly disconnect from the TOR Control Port.", ex);
			}
			finally
			{
				Util.Semaphore.Release();
			}
		}

		private async Task InitializeConnectSocketAsync()
		{
			try
			{
				await Util.Semaphore.WaitAsync().ConfigureAwait(false);
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				await _socket.ConnectAsync(_controlEndPoint).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				throw new TorException("Couldn't connect to the TOR Control Port.", ex);
			}
		}
	}
}