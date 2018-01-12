using DotNetEssentials.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace DotNetTor.Tests
{
	public class SharedFixture : IDisposable
	{
		public string HostAddress { get; set; }
		public int SocksPort { get; set; }
		public int ControlPort { get; set; }
		public string ControlPortPassword { get; set; }
		public IPEndPoint TorSock5EndPoint => new IPEndPoint(IPAddress.Parse(HostAddress), SocksPort);

		public SharedFixture()
		{
			// Initialize tests...

			HostAddress = "127.0.0.1";
			SocksPort = 9050;
			ControlPort = 9051;
			ControlPortPassword = "ILoveBitcoin21";

			Logger.SetMinimumLevel(LogLevel.Trace);
			Logger.SetModes(LogMode.Debug, LogMode.File);
			Logger.SetFilePath("TestLogs.txt");
		}

		public void Dispose()
		{
			// Cleanup tests...
		}
	}
}
