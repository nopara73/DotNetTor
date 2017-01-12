using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotNetTor.ControlPort
{
	/// <summary>
	/// A class containing methods for interacting with a control connection for a tor application.
	/// </summary>
	internal sealed class Connection : IDisposable
	{
		private const string EOL = "\r\n";
		private volatile bool _disposed;
		private StreamReader _reader;
		private Socket _socket;
		private NetworkStream _stream;
		private readonly IPEndPoint _endpoint;
		public string Address => _endpoint.Address.ToString();
		public int ControlPort => _endpoint.Port;

		/// <summary>
		/// Initializes a new instance of the <see cref="Connection"/> class.
		/// </summary>
		public Connection(IPEndPoint endpoint)
		{
			_disposed = false;
			_reader = null;
			_socket = null;
			_stream = null;
			_endpoint = endpoint;
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Connection"/> class.
		/// </summary>
		~Connection()
		{
			Dispose(false);
		}

		#region System.IDisposable

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Releases unmanaged and - optionally - managed resources.
		/// </summary>
		/// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
		private void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				if (_reader != null)
				{
					_reader.Dispose();
					_reader = null;
				}

				if (_stream != null)
				{
					_stream.Dispose();
					_stream = null;
				}

				if (_socket != null)
				{
					if (_socket.Connected)
						_socket.Shutdown(SocketShutdown.Both);

					_socket.Dispose();
					_socket = null;
				}

				_disposed = true;
			}
		}

		#endregion System.IDisposable

		/// <summary>
		/// Authenticates the connection by sending the password to the control port.
		/// </summary>
		/// <param name="password">The password used for authentication.</param>
		/// <returns><c>true</c> if the authentication succeeds; otherwise, <c>false</c>.</returns>
		public bool Authenticate(string password)
		{
			if (_disposed)
				throw new ObjectDisposedException("this");

			if (password == null)
				password = "";

			if (Write("authenticate \"{0}\"", password))
			{
				ConnectionResponse response = Read();

				if (response.Success)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Connects to the control port hosted by the client.
		/// </summary>
		/// <returns><c>true</c> if the connection succeeds; otherwise, <c>false</c>.</returns>
		public bool Connect()
		{
			if (_disposed)
				throw new ObjectDisposedException("this");

			try
			{
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_socket.Connect(Address, ControlPort);

				_stream = new NetworkStream(_socket, false)
				{
					ReadTimeout = 2000
				};

				_reader = new StreamReader(_stream);

				return true;
			}
			catch
			{
				if (_reader != null)
				{
					_reader.Dispose();
					_reader = null;
				}

				if (_stream != null)
				{
					_stream.Dispose();
					_stream = null;
				}

				if (_socket != null)
				{
					if (_socket.Connected)
						_socket.Shutdown(SocketShutdown.Both);

					_socket.Dispose();
					_socket = null;
				}

				return false;
			}
		}

		/// <summary>
		/// Reads a response buffer from the control connection. This method is blocking with a receive timeout of 500ms.
		/// </summary>
		/// <returns>A <see cref="ConnectionResponse"/> containing the response information.</returns>
		public ConnectionResponse Read()
		{
			if (_disposed)
				throw new ObjectDisposedException("this");
			if (_socket == null || _stream == null || _reader == null)
				return new ConnectionResponse(StatusCode.Unknown);

			try
			{
				string line = _reader.ReadLine();

				if (line == null)
					return new ConnectionResponse(StatusCode.Unknown);

				if (line.Length < 3)
					return new ConnectionResponse(StatusCode.Unknown);

				int code;

				if (!int.TryParse(line.Substring(0, 3), out code))
					return new ConnectionResponse(StatusCode.Unknown);

				line = line.Substring(3);

				if (line.Length == 0)
					return new ConnectionResponse((StatusCode)code, new List<string> { "" });

				if (line[0] != '+' && line[0] != '-')
				{
					if (line[0] == ' ')
						line = line.Substring(1);

					return new ConnectionResponse((StatusCode)code, new List<string> { line });
				}

				char id = line[0];

				var responses = new List<string>();
				responses.Add(line.Substring(1));

				try
				{
					for (line = _reader.ReadLine(); line != null; line = _reader.ReadLine())
					{
						var temp1 = line.Trim();
						var temp2 = temp1;

						if (temp1.Length == 0)
							continue;
						if (id == '-' && temp2.Length > 3 && temp2[3] == ' ')
							break;

						if (temp1.Length > 3 && id != '+')
							temp1 = temp1.Substring(4);

						responses.Add(temp1);

						if (id == '+' && ".".Equals(temp1))
							break;
					}
				}
				catch
				{
				}

				return new ConnectionResponse((StatusCode)code, responses);
			}
			catch
			{
				return new ConnectionResponse(StatusCode.Unknown);
			}
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="command">The command to write to the connection.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(string command)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));

			if (!command.EndsWith(EOL, StringComparison.Ordinal))
				command += EOL;

			return Write(Encoding.ASCII.GetBytes(command));
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="command">The command to write to the connection.</param>
		/// <param name="parameters">An optional collection of parameters to serialize into the command.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(string command, params object[] parameters)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));

			command = string.Format(command, parameters);

			if (!command.EndsWith(EOL, StringComparison.Ordinal))
				command += EOL;

			return Write(Encoding.ASCII.GetBytes(command));
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="buffer">The buffer containing the command data.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(byte[] buffer)
		{
			if (_disposed)
				throw new ObjectDisposedException("this");
			if (buffer == null || buffer.Length == 0)
				throw new ArgumentNullException(nameof(buffer));
			if (_socket == null || _stream == null || _reader == null)
				return false;

			try
			{
				_stream.Write(buffer, 0, buffer.Length);
				_stream.Flush();

				return true;
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>
	/// A class containing information regarding a response received back from a control connection.
	/// </summary>
	internal sealed class ConnectionResponse
	{
		private readonly StatusCode code;
		private readonly ReadOnlyCollection<string> responses;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionResponse"/> class.
		/// </summary>
		/// <param name="code">The status code returned by the control connection.</param>
		public ConnectionResponse(StatusCode code)
		{
			this.code = code;
			responses = new List<string>().AsReadOnly();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionResponse"/> class.
		/// </summary>
		/// <param name="code">The status code returned by the control connection.</param>
		/// <param name="responses">The responses received back from the control connection.</param>
		public ConnectionResponse(StatusCode code, IList<string> responses)
		{
			this.code = code;
			this.responses = new ReadOnlyCollection<string>(responses);
		}

		#region Properties

		/// <summary>
		/// Gets a read-only collection of responses received from the control connection.
		/// </summary>
		public ReadOnlyCollection<string> Responses
		{
			get { return responses; }
		}

		/// <summary>
		/// Gets the status code returned with the response.
		/// </summary>
		public StatusCode StatusCode
		{
			get { return code; }
		}

		/// <summary>
		/// Gets a value indicating whether the response was successful feedback.
		/// </summary>
		public bool Success
		{
			get { return code == StatusCode.OK; }
		}

		#endregion Properties
	}
}