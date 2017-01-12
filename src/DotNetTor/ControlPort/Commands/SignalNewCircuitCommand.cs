namespace DotNetTor.ControlPort.Commands
{
	/// <summary>
	/// A class containing the command to generate a new circuit.
	/// </summary>
	internal sealed class SignalNewCircuitCommand : Command<CommandResponse>
	{
		#region Tor.Controller.Command<>

		/// <summary>
		/// Dispatches the command to the client control port and produces a <typeparamref /> response result.
		/// </summary>
		/// <param name="connection">The control connection where the command should be dispatched.</param>
		/// <returns>
		/// A  object instance containing the response data.
		/// </returns>
		protected override CommandResponse Dispatch(Connection connection)
		{
			if (connection.Write("signal newnym"))
			{
				ConnectionResponse response = connection.Read();
				return new CommandResponse(response.Success);
			}

			return new CommandResponse(false);
		}

		#endregion Tor.Controller.Command<>
	}
}