using DotNetEssentials.Logging;
using DotNetTor.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.Tests
{
	public class SharedFixture : IDisposable
	{
		public string HostAddress { get; set; }
		public int SocksPort { get; set; }
		public int ControlPort { get; set; }
		public string ControlPortPassword { get; set; }
		public IPEndPoint TorSock5EndPoint => new IPEndPoint(IPAddress.Parse(HostAddress), SocksPort);

		private Process TorProcess { get; set; }

		public SharedFixture()
		{
			// Initialize tests...

			Logger.SetMinimumLevel(LogLevel.Trace);
			Logger.SetModes(LogMode.Debug, LogMode.File);
			Logger.SetFilePath("TestLogs.txt");

			HostAddress = "127.0.0.1";
			SocksPort = 9050;
			ControlPort = 9051;
			ControlPortPassword = "ILoveBitcoin21";

			TorProcess = null;
			var torControl = new TorControlClient(HostAddress, ControlPort, ControlPortPassword);
			try
			{
				torControl.IsCircuitEstablishedAsync().GetAwaiter().GetResult();
			}
			catch
			{

				var torProcessStartInfo = new ProcessStartInfo("tor")
				{
					Arguments = $"SOCKSPort {SocksPort} ControlPort {ControlPort} HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true
				};

				TorProcess = Process.Start(torProcessStartInfo);

				Task.Delay(3000).GetAwaiter().GetResult();
				var established = false;
				var count = 0;
				while (!established)
				{
					if (count >= 21) throw new TorException("Couldn't establish circuit in time.");
					established = torControl.IsCircuitEstablishedAsync().GetAwaiter().GetResult();
					Task.Delay(1000).GetAwaiter().GetResult();
					count++;
				}
			}
		}

		public void Dispose()
		{
			// Cleanup tests...

			TorProcess?.Kill();
		}
	}
}
