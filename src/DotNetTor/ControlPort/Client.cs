using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.ControlPort
{
	public class Client
	{
		private readonly IPEndPoint _controlEndPoint;
		private readonly string _password;

		public Client(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			_controlEndPoint = new IPEndPoint(IPAddress.Parse(address), controlPort);
			_password = password;
		}

		private async Task AssertControlPortPasswordAsync()
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				await socket.ConnectAsync(_controlEndPoint).ConfigureAwait(false);
				var command = new ArraySegment<byte>(Encoding.ASCII.GetBytes($"authenticate \"{_password}\"\r\n"));

				await socket.SendAsync(command, SocketFlags.None).ConfigureAwait(false);

				var buffer = new ArraySegment<byte>(new byte[128]);
				var received = await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);

				if (received != 0)
				{
					var response = Encoding.ASCII.GetString(buffer.Array, 0, received);

					if (!response.StartsWith("250", StringComparison.Ordinal))
						throw new TorException("Possibly wrong TOR control port password");
				}
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated)]
		public void ChangeCircuit()
		{
			ChangeCircuitAsync().Wait(); // Task.Wait is fine, because the method is obsolated
		}

		public async Task<bool> TryChangeCircuitAsync()
		{
			try
			{
				await TryChangeCircuitAsync().ConfigureAwait(false);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public async Task ChangeCircuitAsync()
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				try
				{
					// 1. CONNECT
					await socket.ConnectAsync(_controlEndPoint).ConfigureAwait(false);

					// 2. AUTHENTICATE
					var command = new ArraySegment<byte>(Encoding.ASCII.GetBytes($"authenticate \"{_password}\"\r\n"));
					await socket.SendAsync(command, SocketFlags.None).ConfigureAwait(false);

					var buffer = new ArraySegment<byte>(new byte[128]);
					var received = await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);

					var response = Encoding.ASCII.GetString(buffer.Array, 0, received);
					var statusCode = GetStatusCode(response);
					if (statusCode != StatusCode.OK)
						throw new TorException($"Possibly wrong TOR control port password. {nameof(statusCode)}: {statusCode}");

					// 3. CHANGE CIRCUIT
					command = new ArraySegment<byte>(Encoding.ASCII.GetBytes("signal newnym\r\n"));
					await socket.SendAsync(command, SocketFlags.None).ConfigureAwait(false);

					buffer = new ArraySegment<byte>(new byte[128]);
					received = await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);

					response = Encoding.ASCII.GetString(buffer.Array, 0, received);
					statusCode = GetStatusCode(response);
					if (statusCode != StatusCode.OK)
						throw new TorException($"Couldn't change the circuit. {nameof(statusCode)}: {statusCode}");

				}
				catch (Exception ex)
				{
					throw new TorException("Couldn't change circuit", ex);
				}
				finally
				{
					if(socket.Connected)
						socket.Shutdown(SocketShutdown.Both);
				}
			}
		}

		private static StatusCode GetStatusCode(string response)
		{
			StatusCode statusCode;
			try
			{
				statusCode = (StatusCode)int.Parse(response.Substring(0, 3));
			}
			catch (Exception ex)
			{
				throw new TorException("Wrong response", ex);
			}

			return statusCode;
		}
	}
}