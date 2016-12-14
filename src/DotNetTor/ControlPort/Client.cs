using DotNetTor.ControlPort.Commands;
using System;
using System.Net.Sockets;
using System.Text;

namespace DotNetTor.ControlPort
{
    public class Client
    {
		private string _address;
		private int _controlPort;
		private string _password;

		public Client(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			Util.AssertPortOpen(address, controlPort);
			AssertControlPortPassword(address, controlPort, password);

			_address = address;
			_controlPort = controlPort;
			_password = password;
		}

		private static void AssertControlPortPassword(string address, int controlPort, string password)
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				socket.Connect(address, controlPort);
				byte[] command = Encoding.ASCII.GetBytes($"authenticate \"{password}\"\r\n");
				socket.Send(command);
				byte[] buffer = new byte[128];
				int received = socket.Receive(buffer);
				if (received != 0)
				{
					string response = Encoding.ASCII.GetString(buffer, 0, received);

					if (!response.StartsWith("250", StringComparison.Ordinal))
						throw new TorException("Possibly wrong TOR control port password");
				}
			}
		}

		/// <summary>
		/// Cleans the current circuits in the tor application by requesting new circuits be generated.
		/// </summary>
		public bool ChangeCircuit()
		{
			try
			{
				return Command<CommandResponse>.DispatchAndReturn<SignalNewCircuitCommand>(_address, _controlPort, _password);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}
	}
}
