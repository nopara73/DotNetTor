using DotNetTor.ControlPort.Commands;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.ControlPort
{
    public class Client
    {
		private IPEndPoint _controlEndPoint;
		private string _password;

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
				byte[] command = Encoding.ASCII.GetBytes($"authenticate \"{_password}\"\r\n");
				await Task.Run(() => socket.Send(command)).ConfigureAwait(false);
				byte[] buffer = new byte[128];
				int received = await Task.Run(() => socket.Receive(buffer)).ConfigureAwait(false);
				if (received != 0)
				{
					string response = Encoding.ASCII.GetString(buffer, 0, received);

					if (!response.StartsWith("250", StringComparison.Ordinal))
						throw new TorException("Possibly wrong TOR control port password");
				}
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated)]
		public bool ChangeCircuit()
		{
			return ChangeCircuitAsync().Result; // Task.Result is fine, because the method is obsolated
		}
		/// <summary>
		/// Cleans the current circuits in the tor application by requesting new circuits be generated.
		/// </summary>
		public async Task<bool> ChangeCircuitAsync()
		{
			await Util.AssertPortOpenAsync(_controlEndPoint).ConfigureAwait(false);
			await AssertControlPortPasswordAsync().ConfigureAwait(false);

			try
			{
				return Command<CommandResponse>.DispatchAndReturn<SignalNewCircuitCommand>(_controlEndPoint, _password);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}
	}
}
