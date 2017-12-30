using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.ControlPort
{
	public sealed class TorControlClient
	{
		public static event EventHandler CircuitChangeRequested;
		public static void OnCircuitChangeRequested() => CircuitChangeRequested?.Invoke(null, EventArgs.Empty);

		private readonly IPEndPoint _controlEndPoint;
		private readonly byte[] _authenticationToken;
		private readonly string _cookieFilePath;
		private Socket _socket;
		
		/// <param name="password">UTF8</param>
		public TorControlClient(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			_controlEndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			if (password == "") _authenticationToken = null;
			else _authenticationToken = Encoding.UTF8.GetBytes(password);
		}
		
		public TorControlClient(string address, int controlPort, FileInfo cookieFile)
		{
			_controlEndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			_cookieFilePath = cookieFile.FullName;
			_authenticationToken = null;
		}

		public async Task<bool> IsCircuitEstablishedAsync(CancellationToken ctsToken = default)
		{
			using (await Util.AsyncLock.LockAsync(ctsToken).ConfigureAwait(false))
			{
				// Get info
				var response = await SendCommandAsync("GETINFO status/circuit-established", ctsToken: ctsToken).ConfigureAwait(false);

				if (response.Contains("status/circuit-established=1", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				else if (response.Contains("status/circuit-established=0", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				else throw new TorException($"Wrong response to 'GETINFO status/circuit-established': '{response}'");
			}
		}

		public async Task ChangeCircuitAsync(CancellationToken ctsToken = default)
		{
			using (await Util.AsyncLock.LockAsync(ctsToken).ConfigureAwait(false))
			{
				try
				{
					OnCircuitChangeRequested();

					await InitializeConnectSocketAsync(ctsToken).ConfigureAwait(false);

					await AuthenticateAsync(ctsToken).ConfigureAwait(false);

					// Subscribe to SIGNAL events
					await SendCommandAsync("SETEVENTS SIGNAL", initAuthDispose: false, ctsToken: ctsToken).ConfigureAwait(false);

					// Clear all existing circuits and build new ones
					await SendCommandAsync("SIGNAL NEWNYM", initAuthDispose: false, ctsToken: ctsToken).ConfigureAwait(false);

					// Unsubscribe from all events
					await SendCommandAsync("SETEVENTS", initAuthDispose: false, ctsToken: ctsToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					throw new TorException("Couldn't change circuit", ex);
				}
				finally
				{
					DisconnectDisposeSocket();

					// safety delay, in case the tor client is not quick enough with the actions
					await Task.Delay(100, ctsToken).ConfigureAwait(false);
				}
			}
		}

		public async Task AuthenticateAsync(CancellationToken ctsToken)
		{
			string authString = "\"\"";
			if (_authenticationToken != null)
			{
				authString = Util.ByteArrayToString(_authenticationToken);
			}
			else if (_cookieFilePath != null && _cookieFilePath != "")
			{
				authString = Util.ByteArrayToString(File.ReadAllBytes(_cookieFilePath));
			}
			await SendCommandAsync($"AUTHENTICATE {authString}", initAuthDispose: false, ctsToken: ctsToken).ConfigureAwait(false);
		}

		private static HashSet<string> _eventsSet = new HashSet<string>();

		/// <summary>
		/// Waits for the specified event. Also consumes all responses.
		/// </summary>
		/// <param name="eventStartsWith"></param>
		/// <param name="timeout"></param>
		/// <returns>Full event</returns>
		private async Task<string> WaitForEventAsync(string eventStartsWith, TimeSpan timeout, CancellationToken ctsToken)
		{
			var timeoutTask = Task.Delay(timeout, ctsToken);
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

		public async Task<string> SendCommandAsync(string command, CancellationToken ctsToken = default)
		{
			return await SendCommandAsync(command, initAuthDispose: true, ctsToken: ctsToken).ConfigureAwait(false);
		}

		public async Task<string> SendCommandAsync(string command, bool initAuthDispose, CancellationToken ctsToken = default)
		{
			try
			{
				if (initAuthDispose)
				{
					await InitializeConnectSocketAsync(ctsToken).ConfigureAwait(false);

					await AuthenticateAsync(ctsToken).ConfigureAwait(false);
				}

				try
				{
					command = command.Trim();
					if (!command.EndsWith("\r\n", StringComparison.Ordinal))
					{
						command += "\r\n";
					}

					var commandByteArraySegment = new ArraySegment<byte>(Encoding.ASCII.GetBytes(command));
					await _socket.SendAsync(commandByteArraySegment, SocketFlags.None).ConfigureAwait(false);
					ctsToken.ThrowIfCancellationRequested();
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
					var responseLines = new List<string>(response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
					
					// error check a few commands I use
					if(command.StartsWith("AUTHENTICATE", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("SETEVENTS", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("SIGNAL", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("GETINFO", StringComparison.OrdinalIgnoreCase))
					{
						if (!responseLines.Any(x => x.StartsWith("250 OK", StringComparison.OrdinalIgnoreCase)))
						throw new TorException(
							$"Unexpected {nameof(response)} from TOR Control Port to sent {nameof(command)} : {command} , {nameof(response)} : {response}");

					}

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
									await WaitForEventAsync("650 SIGNAL NEWNYM", TimeSpan.FromMinutes(3), ctsToken).ConfigureAwait(false);
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

					return response;
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
			finally
			{
				if (initAuthDispose)
				{
					DisconnectDisposeSocket();

					// safety delay, in case the tor client is not quick enough with the actions
					await Task.Delay(100).ConfigureAwait(false);
				}
			}
		}

		/// <summary>
		/// Always use it in finally block.
		/// </summary>
		public void DisconnectDisposeSocket()
		{
			try
			{
				if (_socket != null)
				{
					if (_socket.Connected)
					{
						_socket.Shutdown(SocketShutdown.Both);
					}
					_socket.Dispose();
				}
			}
			catch
			{
				// ignore
			}
		}

		public async Task InitializeConnectSocketAsync(CancellationToken ctsToken)
		{
			try
			{
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