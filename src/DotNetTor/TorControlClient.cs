using DotNetTor.Exceptions;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
	public sealed class TorControlClient
	{
		public static event EventHandler CircuitChangeRequested;
		public static void OnCircuitChangeRequested() => CircuitChangeRequested?.Invoke(null, EventArgs.Empty);

		public IPEndPoint EndPoint;
		private readonly byte[] _authenticationToken;
		private readonly string _cookieFilePath;
		public TcpClient TcpClient;

		private static AsyncLock AsyncLock { get; } = new AsyncLock();

		/// <param name="password">UTF8</param>
		public TorControlClient(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			EndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			if (password == "") _authenticationToken = null;
			else _authenticationToken = Encoding.UTF8.GetBytes(password);
		}
		
		public TorControlClient(string address, int controlPort, FileInfo cookieFile)
		{
			EndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			_cookieFilePath = cookieFile.FullName;
			_authenticationToken = null;
		}

		public async Task<bool> IsCircuitEstablishedAsync(CancellationToken ctsToken = default)
		{
			using (await AsyncLock.LockAsync(ctsToken).ConfigureAwait(false))
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
				else throw new TorException($"Wrong response to 'GETINFO status/circuit-established': '{response}'.");
			}
		}

		public async Task ChangeCircuitAsync(CancellationToken ctsToken = default)
		{
			using (await AsyncLock.LockAsync(ctsToken).ConfigureAwait(false))
			{
				try
				{
					OnCircuitChangeRequested();

					await InitializeConnectTcpConnectionAsync(ctsToken).ConfigureAwait(false);

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
					throw new TorException("Couldn't change circuit.", ex);
				}
				finally
				{
					DisposeTcpClient();

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
				authString = ByteHelpers.ToHex(_authenticationToken);
			}
			else if (_cookieFilePath != null && _cookieFilePath != "")
			{
				authString = ByteHelpers.ToHex(File.ReadAllBytes(_cookieFilePath));
			}
			await SendCommandAsync($"AUTHENTICATE {authString}", initAuthDispose: false, ctsToken: ctsToken).ConfigureAwait(false);
		}

		private static HashSet<string> _eventsSet = new HashSet<string>();

		/// <summary>
		/// Waits for the specified event. Also consumes all responses.
		/// </summary>
		/// <returns>Full event</returns>
		private async Task<string> WaitForEventAsync(string code, string command, TimeSpan timeout, CancellationToken ctsToken)
		{
			var timeoutTask = Task.Delay(timeout, ctsToken);
			while (true)
			{
				var buffer = new byte[TcpClient.ReceiveBufferSize];
				Task<int> receiveTask = TcpClient.GetStream().ReadAsync(buffer, 0, buffer.Length);

				var firstTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

				if (firstTask == timeoutTask) throw new TimeoutException($"Did not receive the expected {nameof(code)} and {nameof(command)} : { code } { command } within the specified {nameof(timeout)} : {timeout}.");

				int receivedCount = await receiveTask.ConfigureAwait(false);

				if (receivedCount <= 0)
				{
					throw new InvalidOperationException("Not connected to Tor Control port.");
				}

				var response = Encoding.ASCII.GetString(buffer, 0, receivedCount);
				if (response.StartsWith($"{code} {command}", StringComparison.OrdinalIgnoreCase) || response.StartsWith($"{code}-{command}", StringComparison.OrdinalIgnoreCase))
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
					await InitializeConnectTcpConnectionAsync(ctsToken).ConfigureAwait(false);

					await AuthenticateAsync(ctsToken).ConfigureAwait(false);
				}
				var stream = TcpClient.GetStream();

				try
				{
					command = command.Trim();
					if (!command.EndsWith("\r\n", StringComparison.Ordinal))
					{
						command += "\r\n";
					}

					var commandBytes = Encoding.ASCII.GetBytes(command);
					await stream.WriteAsync(commandBytes, 0, commandBytes.Length).ConfigureAwait(false);
					await stream.FlushAsync().ConfigureAwait(false);
					ctsToken.ThrowIfCancellationRequested();
				}
				catch (Exception ex)
				{
					throw new TorException($"Failed to send command to Tor Control Port: {nameof(command)} : {command}.", ex);
				}

				var bufferBytes = new byte[TcpClient.ReceiveBufferSize];
				try
				{
					var receivedCount = await stream.ReadAsync(bufferBytes, 0, bufferBytes.Length).ConfigureAwait(false);
					if (receivedCount <= 0)
					{
						throw new InvalidOperationException("Not connected to Tor Control port.");
					}

					var response = Encoding.ASCII.GetString(bufferBytes, 0, receivedCount);
					var responseLines = new List<string>(response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
					
					// error check a few commands I use
					if(command.StartsWith("AUTHENTICATE", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("SETEVENTS", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("SIGNAL", StringComparison.OrdinalIgnoreCase)
						|| command.StartsWith("GETINFO", StringComparison.OrdinalIgnoreCase))
					{
						if (!responseLines.Any(x => x.StartsWith("250 OK", StringComparison.OrdinalIgnoreCase) || x.StartsWith("250-OK", StringComparison.OrdinalIgnoreCase)))
						throw new TorException(
							$"Unexpected {nameof(response)} from Tor Control Port to sent {nameof(command)} : {command} , {nameof(response)} : {response}.");

					}

					// If we are tracking the signal events throw exception if didn't get the expected response
					if (command.StartsWith("SIGNAL", StringComparison.OrdinalIgnoreCase))
					{
						if (_eventsSet.Any(x => x.Equals("SIGNAL", StringComparison.OrdinalIgnoreCase)))
						{
							command = command.Replace("\r\n", "");
							var what = new List<string>(command.Split(' '))[1];
							if (!responseLines.Any(x => x.StartsWith($"650 SIGNAL {what}", StringComparison.OrdinalIgnoreCase) || x.StartsWith($"650-SIGNAL {what}", StringComparison.OrdinalIgnoreCase)))
							{
								// NEWNYM may be rate-limited (usually around 10 seconds, the max I've seen is 2 minutes)
								if (what.StartsWith("NEWNYM", StringComparison.OrdinalIgnoreCase))
								{
									await WaitForEventAsync("650", "SIGNAL NEWNYM", TimeSpan.FromMinutes(3), ctsToken).ConfigureAwait(false);
								}
								else
								{
									throw new TorException(
										$"Didn't receive 650 SIGNAL {what} confirmation from Tor Control Port to sent {nameof(command)} : {command} , {nameof(response)} : {response}.");
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
						$"Didn't receive response for the sent {nameof(command)} from Tor Control Port: {nameof(command)} : {command}.", ex);
				}
			}
			finally
			{
				if (initAuthDispose)
				{
					DisposeTcpClient();

					// safety delay, in case the tor client is not quick enough with the actions
					await Task.Delay(100).ConfigureAwait(false);
				}
			}
		}

		public void DisposeTcpClient()
		{
			if(TcpClient != null)
			{
				if(TcpClient.Connected)
				{
					TcpClient.GetStream()?.Dispose();
				}
				TcpClient.Dispose();
			}
			TcpClient = null; // without this, canchangecircuitmultiple times test will fail
		}

		public async Task InitializeConnectTcpConnectionAsync(CancellationToken ctsToken)
		{
			try
			{
				if (TcpClient == null)
				{
					TcpClient = new TcpClient();
				}
				await TcpClient.ConnectAsync(EndPoint.Address, EndPoint.Port).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				throw new TorException("Couldn't connect to the Tor Control Port.", ex);
			}
		}
	}
}