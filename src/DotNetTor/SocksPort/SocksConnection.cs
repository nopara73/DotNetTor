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
		public Socket Socket;
		public Stream Stream;
		public volatile int ReferenceCount;
		private AsyncLock _asyncLock;

		public SocksConnection()
		{
			EndPoint = null;
			_asyncLock = new AsyncLock();
		}

		private void HandshakeTor()
		{
			var sendBuffer = new byte[] { 5, 1, 0 };
			Socket.Send(sendBuffer, SocketFlags.None);

			var recBuffer = new byte[Socket.ReceiveBufferSize];
			var recCnt = Socket.Receive(recBuffer, SocketFlags.None);

			Util.ValidateHandshakeResponse(recBuffer, recCnt);
		}


		private void ConnectSocket()
		{
			Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
			{
				Blocking = true
			};
			Socket.Connect(EndPoint);
		}

		private async Task ConnectToDestinationAsync(CancellationToken ctsToken = default)
		{
			var sendBuffer = new ArraySegment<byte>(Util.BuildConnectToUri(Destination).Array);
			await Socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
			ctsToken.ThrowIfCancellationRequested();

			var recBuffer = new ArraySegment<byte>(new byte[Socket.ReceiveBufferSize]);
			var recCnt = await Socket.ReceiveAsync(recBuffer, SocketFlags.None).ConfigureAwait(false);
			ctsToken.ThrowIfCancellationRequested();

			Util.ValidateConnectToDestinationResponse(recBuffer.Array, recCnt);

			Stream stream = new NetworkStream(Socket, ownsSocket: false);
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

		private bool IsSocketConnected(bool throws)
		{
			try
			{
				if (Socket == null)
					return false;
				if (!Socket.Connected)
					return false;
				//if (Socket.Available == 0)
				//	return false;
				//if (Socket.Poll(1000, SelectMode.SelectRead))
				//	return false;

				return true;
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

				var requestString = await request.ToHttpStringAsync(ctsToken).ConfigureAwait(false);
				ctsToken.ThrowIfCancellationRequested();

				await Stream.WriteAsync(Encoding.UTF8.GetBytes(requestString), 0, requestString.Length, ctsToken).ConfigureAwait(false);
				await Stream.FlushAsync(ctsToken).ConfigureAwait(false);
				ctsToken.ThrowIfCancellationRequested();

				return await new HttpResponseMessage().CreateNewAsync(Stream, request.Method, ctsToken).ConfigureAwait(false);
			}
			catch (SocketException)
			{
				DestroySocket();
				throw;
			}
		}

		private async Task EnsureConnectedToTorAsync(CancellationToken ctsToken = default)
		{
			if (!IsSocketConnected(throws: false)) // Socket.Connected is misleading, don't use that
			{
				DestroySocket();
				ConnectSocket();
				HandshakeTor();
				await ConnectToDestinationAsync(ctsToken);
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
						DestroySocket();
						disposed = true;
					}
				}
			}
			catch
			{
				// ignored
			}
		}

		private void DestroySocket()
		{
			try
			{
				if (Stream != null)
				{
					Stream.Dispose();
					Stream = null;
				}
				if (Socket != null)
				{
					try
					{
						Socket.Shutdown(SocketShutdown.Both);
					}
					catch (SocketException) { }
					Socket.Dispose();
					Socket = null;
				}
			}
			catch
			{
				// ignore
			}
		}
	}
}
