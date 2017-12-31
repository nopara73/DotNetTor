using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort
{
	internal sealed class SocksConnection
	{
		public IPEndPoint EndPoint;
		public Uri Destination;
		public TcpClient TcpClient;
		public Stream Stream;
		public volatile int ReferenceCount;
		private AsyncLock _asyncLock;

		public SocksConnection()
		{
			EndPoint = null;
			_asyncLock = new AsyncLock();
			TcpClient = null;
		}

		private async Task HandshakeTorAsync()
		{
			var stream = TcpClient.GetStream();
			var sendBuffer = new byte[] { 5, 1, 0 };

			await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).ConfigureAwait(false);
			await stream.FlushAsync().ConfigureAwait(false);

			var recBuffer = new byte[TcpClient.ReceiveBufferSize];
				
			var recCnt = await stream.ReadAsync(recBuffer, 0, recBuffer.Length).ConfigureAwait(false);

			if (recCnt <= 0)
			{
				throw new InvalidOperationException("Not connected to Tor Socks port");
			}

			Util.ValidateHandshakeResponse(recBuffer, recCnt);
		}
		
		private async Task ConnectToSocksAsync()
		{
			if (TcpClient == null)
			{
				TcpClient = new TcpClient();
			}
			await TcpClient.ConnectAsync(EndPoint.Address, EndPoint.Port).ConfigureAwait(false);
		}

		private async Task ConnectToDestinationAsync(CancellationToken ctsToken = default)
		{
			Stream stream = TcpClient.GetStream();
			
			var sendBuffer = Util.BuildConnectToUri(Destination).Array;
			await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).ConfigureAwait(false);
			await stream.FlushAsync().ConfigureAwait(false);
			ctsToken.ThrowIfCancellationRequested();

			var recBuffer = new byte[TcpClient.ReceiveBufferSize];

			var recCnt = await stream.ReadAsync(recBuffer, 0, recBuffer.Length).ConfigureAwait(false);

			if (recCnt <= 0)
			{
				throw new InvalidOperationException("Not connected to Tor Socks port");
			}

			ctsToken.ThrowIfCancellationRequested();

			Util.ValidateConnectToDestinationResponse(recBuffer, recCnt);
			
			if (Destination.Scheme.Equals("https", StringComparison.Ordinal))
			{
				SslStream httpsStream;
				// On Linux and OSX ignore certificate, because of a .NET Core bug
				// This is a security vulnerability, has to be fixed as soon as the bug get fixed
				// Details:
				// https://github.com/dotnet/corefx/issues/21761
				// https://github.com/nopara73/DotNetTor/issues/4
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					httpsStream = new SslStream(
						stream,
						leaveInnerStreamOpen: true);
				}
				else
				{
					httpsStream = new SslStream(
						stream,
						leaveInnerStreamOpen: true,
						userCertificateValidationCallback: (a, b, c, d) => true);
				}

				await httpsStream
					.AuthenticateAsClientAsync(
						Destination.DnsSafeHost,
						new X509CertificateCollection(),
						SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
						checkCertificateRevocation: true)
					.ConfigureAwait(false);
				stream = httpsStream;
			}
			Stream = stream;
		}

		private bool IsSocksConnected(bool throws)
		{
			try
			{
				if (TcpClient == null) return false;
				return TcpClient.Connected;
			}
			catch
			{
				if (throws)
					throw;

				return false;
			}
		}

		public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ctsToken)
		{
			try
			{
				await EnsureConnectedToTorAsync(ctsToken).ConfigureAwait(false);
				ctsToken.ThrowIfCancellationRequested();

				// https://tools.ietf.org/html/rfc7230#section-3.3.2
				// A user agent SHOULD send a Content - Length in a request message when
				// no Transfer-Encoding is sent and the request method defines a meaning
				// for an enclosed payload body.For example, a Content - Length header
				// field is normally sent in a POST request even when the value is 0
				// (indicating an empty payload body).A user agent SHOULD NOT send a
				// Content - Length header field when the request message does not contain
				// a payload body and the method semantics do not anticipate such a
				// body.
				// TODO implement it fully (altough probably .NET already ensures it)
				if (request.Method == HttpMethod.Post)
				{
					if (request.Headers.TransferEncoding.Count == 0)
					{
						if (request.Content == null)
						{
							request.Content = new ByteArrayContent(new byte[] { }); // dummy empty content
							request.Content.Headers.ContentLength = 0;
						}
						else
						{
							if (request.Content.Headers.ContentLength == null)
							{
								request.Content.Headers.ContentLength = (await request.Content.ReadAsStringAsync().ConfigureAwait(false)).Length;
							}
						}
					}
				}

				var requestString = await request.ToHttpStringAsync().ConfigureAwait(false);
				ctsToken.ThrowIfCancellationRequested();

                var bytes = Encoding.UTF8.GetBytes(requestString);
				try
				{
					await Stream.WriteAsync(bytes, 0, bytes.Length, ctsToken).ConfigureAwait(false);
				}
				catch (NullReferenceException) // dotnet brainfart
				{
					throw new OperationCanceledException();
				}
				await Stream.FlushAsync(ctsToken).ConfigureAwait(false);
				ctsToken.ThrowIfCancellationRequested();

				return await new HttpResponseMessage().CreateNewAsync(Stream, request.Method).ConfigureAwait(false);
			}
			catch (SocketException)
			{
				DisposeTcpClient();
				throw;
			}
		}

		private async Task EnsureConnectedToTorAsync(CancellationToken ctsToken = default)
		{
			if (!IsSocksConnected(throws: false)) // TcpClient.Connected is misleading, don't use that
			{
				DisposeTcpClient();
				await ConnectToSocksAsync().ConfigureAwait(false);
				await HandshakeTorAsync().ConfigureAwait(false);
				await ConnectToDestinationAsync(ctsToken).ConfigureAwait(false);
			}
		}

		public void RemoveReference(out bool disposed)
		{
			disposed = false;
			try
			{
				var value = ReferenceCount--;
				if (value == 0)
				{
					using (_asyncLock.Lock())
					{
						DisposeTcpClient();
						disposed = true;
					}
				}
			}
			catch
			{
				// ignored
			}
		}

		private void DisposeTcpClient()
		{
			Stream?.Dispose();
			TcpClient?.Dispose();
			Stream = null;
			TcpClient = null;
		}
	}
}
