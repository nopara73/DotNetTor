using DotNetTor.SocksPort.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;

namespace DotNetTor.SocksPort
{
	[Obsolete(Shared.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
	public sealed class Client : IDisposable
	{
		private readonly IPEndPoint _socksEndPoint;

		[Obsolete(Shared.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		public Client(string address = "127.0.0.1", int socksPort = 9050)
		{
			try
			{
				_socksEndPoint = new IPEndPoint(IPAddress.Parse(address), socksPort);
			}
			catch (Exception ex)
			{
				throw new TorException("SocksPort client initialization failed.", ex);
			}
		}

		[Obsolete(Shared.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public SocksPortHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
			=> new SocksPortHandler(_socksEndPoint.Address.ToString(), _socksEndPoint.Port);

		[Obsolete(Shared.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public SocksPortHandler GetHandlerFromRequestUri(string requestUri = "")
			=> new SocksPortHandler(_socksEndPoint.Address.ToString(), _socksEndPoint.Port);

		private void ReleaseUnmanagedResources()
		{
			// Leave empty to not break userspace
		}

		[Obsolete(Shared.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		public void Dispose()
		{
			ReleaseUnmanagedResources();
			GC.SuppressFinalize(this);
		}

		~Client()
		{
			ReleaseUnmanagedResources();
		}
	}
}