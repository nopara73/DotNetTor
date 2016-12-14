using DotNetTor.ControlPort.Commands;
using System;

namespace DotNetTor.ControlPort
{
    public class Client
    {
		private string _address;
		private int _controlPort;
		private string _password;

		public Client(string address = "127.0.0.1", int controlPort = 9051, string password = "")
		{
			_address = address;
			_controlPort = controlPort;
			_password = password;
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
