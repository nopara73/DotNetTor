using DotNetTor.Exceptions;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
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

		#region Methods

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task<TotContent> SendAsync(TotRequest request)
		{
			Guard.NotNull(nameof(request), request);

			byte[] responseBytes = await TorSocks5Client.SendAsync(request.ToBytes());
			var response = new TotResponse();
			response.FromBytes(responseBytes);

			AssertVersion(request.Version, response.Version);
			AssertSuccess(response);

			return response.Content;
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task SubscribeAsync(string purpose)
		{
			purpose = Guard.NotNullOrEmptyOrWhitespace(nameof(purpose), purpose, trim: true);

			TotSubscribeRequest request = new TotSubscribeRequest(purpose);

			byte[] responseBytes = await TorSocks5Client.SendAsync(request.ToBytes());
			var response = new TotResponse();
			response.FromBytes(responseBytes);

			AssertVersion(request.Version, response.Version);
			AssertSuccess(response);
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task UnsubscribeAsync(TotUnsubscribeRequest request)
		{
			Guard.NotNull(nameof(request), request);

			byte[] responseBytes = await TorSocks5Client.SendAsync(request.ToBytes());
			var response = new TotResponse();
			response.FromBytes(responseBytes);

			AssertVersion(request.Version, response.Version);
			AssertSuccess(response);
		}

		/// <summary>
		/// throws on failure
		/// </summary>
		public async Task PingAsync()
		{
			var ping = TotPing.Instance;
			byte[] responseBytes = await TorSocks5Client.SendAsync(ping.ToBytes());
			var pong = new TotPong();
			pong.FromBytes(responseBytes);

			AssertVersion(ping.Version, pong.Version);
		}

		private static void AssertVersion(TotVersion expected, TotVersion actual)
		{
			if (expected != actual)
			{
				throw new TotRequestException($"Server responded with wrong version. Expected: {expected}. Actual: {actual}.");
			}
		}

		private static void AssertSuccess(TotResponse response)
		{
			if (response != TotResponse.Success)
			{
				string errorMessage = $"Server responded with {response.Purpose}.";
				if (response.Content != TotContent.Empty)
				{
					errorMessage += $" Details: {response.Content}.";
				}
				throw new TotRequestException(errorMessage);
			}
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
