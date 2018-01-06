using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor
{
	/// <summary>
	/// Create an instance with the TorSocks5Manager
	/// </summary>
	public class TorOverTcpClient : IDisposable
    {
		#region PropertiesAndMembers

		public TorSocks5Client TorSocks5Client { get; private set; }

		#endregion

		#region ConstructorsAndInitializers
		
		internal TorOverTcpClient(TorSocks5Client torSocks5Client)
		{
			TorSocks5Client = Guard.NotNull(nameof(torSocks5Client), torSocks5Client);
		}

		#endregion

		#region IDisposable Support

		private volatile bool _disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					TorSocks5Client?.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				_disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~TorOverTcpClient() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}

		#endregion
	}
}
