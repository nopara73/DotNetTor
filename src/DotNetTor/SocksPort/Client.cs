using DotNetTor.SocksPort.Net;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
// ReSharper disable All

namespace DotNetTor.SocksPort
{
	[Obsolete(Util.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
	public sealed class Client : IDisposable
	{
		private readonly IPEndPoint _socksEndPoint;

		[Obsolete(Util.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
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

		[Obsolete(Util.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public SocksPortHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
			=> new SocksPortHandler(_socksEndPoint.Address.ToString(), _socksEndPoint.Port);

		[Obsolete(Util.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")] // It's fine to leave it as, to not break userspace
		public SocksPortHandler GetHandlerFromRequestUri(string requestUri = "")
			=> new SocksPortHandler(_socksEndPoint.Address.ToString(), _socksEndPoint.Port);

		private void ReleaseUnmanagedResources()
		{
			// Leave empty to not break userspace
		}

		[Obsolete(Util.ClassDeprecated + "Consider using SocksPortHandler class instead.")]
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