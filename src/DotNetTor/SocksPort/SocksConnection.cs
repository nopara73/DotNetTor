using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using DotNetTor.SocksPort.Helpers;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort
{
	internal sealed class SocksConnection
	{
		public IPEndPoint EndPoint = null;
		public Uri Destination;
		public Socket Socket;
		public Stream Stream;
		public int ReferenceCount;
		private object _lock = new object();

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

		private void ConnectToDestination(bool ignoreSslCertification = false)
		{
			var sendBuffer = Util.BuildConnectToUri(Destination).Array;
			Socket.Send(sendBuffer, SocketFlags.None);

			var recBuffer = new byte[Socket.ReceiveBufferSize];
			var recCnt = Socket.Receive(recBuffer, SocketFlags.None);

			Util.ValidateConnectToDestinationResponse(recBuffer, recCnt);

			Stream stream = new NetworkStream(Socket, ownsSocket: false);
			if (Destination.Scheme.Equals("https", StringComparison.Ordinal))
			{
				SslStream httpsStream;
				if(ignoreSslCertification)
				{
					httpsStream = new SslStream(
						stream,
						leaveInnerStreamOpen: true,
						userCertificateValidationCallback: (a, b, c, d) => true);
				}
				else
				{
					httpsStream = new SslStream(stream, leaveInnerStreamOpen: true);
				}

				httpsStream
					.AuthenticateAsClientAsync(
						Destination.DnsSafeHost,
						new X509CertificateCollection(),
						SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
						checkCertificateRevocation: true)
					.Wait();
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

		public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ctsToken, bool ignoreSslCertification = false)
		{
			try
			{
				EnsureConnectedToTor(ignoreSslCertification);
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

				Stream.Write(Encoding.UTF8.GetBytes(requestString), 0, requestString.Length);
				Stream.Flush();
				ctsToken.ThrowIfCancellationRequested();

				return await new HttpResponseMessage().CreateNewAsync(Stream, request.Method).ConfigureAwait(false);

				//var reader = new ByteStreamReader(Stream, Socket.ReceiveBufferSize, preserveLineEndings: false);

				//// read the first line of the response
				//string line = reader.ReadLine();
				//var pieces = line.Split(new[] { ' ' }, 3);

				//// According to RFC7230, if the major version is the same recipient must understand
				//if (pieces[0] == null || !pieces[0].StartsWith("HTTP/1."))
				//{
				//	throw new HttpRequestException($"Only HTTP/1.1 is supported, actual: {pieces[0]}");
				//}

				//var statusCode = (HttpStatusCode)int.Parse(pieces[1]);
				//var response = new HttpResponseMessage(statusCode) { RequestMessage = request };
				//response.Version = new Version(pieces[0].Split("/".ToCharArray())[1]);

				//// read the headers
				//response.Content = new ByteArrayContent(new byte[0]);
				//while ((line = reader.ReadLine()) != null && line != string.Empty)
				//{
				//	pieces = line.Split(new[] { ":" }, 2, StringSplitOptions.None);
				//	if (pieces[1].StartsWith(" ", StringComparison.Ordinal))
				//		pieces[1] = pieces[1].Substring(1);

				//	if (!response.Headers.TryAddWithoutValidation(pieces[0], pieces[1]) &&
				//		!response.Content.Headers.TryAddWithoutValidation(pieces[0], pieces[1]))
				//		throw new InvalidOperationException(
				//			$"The header '{pieces[0]}' could not be added to the response message or to the response content.");
				//}

				//if (!(request.Method == new HttpMethod("CONNECT") || request.Method == HttpMethod.Head))
				//{
				//	HttpContent httpContent = null;
				//	if (response.Headers.TransferEncodingChunked.GetValueOrDefault(false))
				//	{
				//		// read the body with chunked transfer encoding
				//		var chunkedStream = new ReadsFromChunksStream(reader.RemainingStream, Socket.ReceiveBufferSize);
				//		httpContent = new StreamContent(chunkedStream);
				//	}
				//	else if (response.Content.Headers.ContentLength.HasValue)
				//	{
				//		// read the body with a content-length
				//		var limitedStream = new LimitedStream(
				//			reader.RemainingStream,
				//			response.Content.Headers.ContentLength.Value);
				//		httpContent = new StreamContent(limitedStream);
				//	}
				//	else return response;

				//	if (response.Content != null)
				//	{
				//		// copy over the content headers
				//		foreach (var header in response.Content.Headers)
				//			httpContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

				//		response.Content = httpContent;
				//	}
				//}

				//return response;
			}
			catch (SocketException)
			{
				DestroySocket();
				throw;
			}
		}

		private void EnsureConnectedToTor(bool ignoreSslCertification)
		{
			if (!IsSocketConnected(throws: false)) // Socket.Connected is misleading, don't use that
			{
				DestroySocket();
				ConnectSocket();
				HandshakeTor();
				ConnectToDestination(ignoreSslCertification);
			}
		}

		public void AddReference() => Interlocked.Increment(ref ReferenceCount);

		public void RemoveReference(out bool disposed)
		{
			disposed = false;
			var value = Interlocked.Decrement(ref ReferenceCount);
			if (value == 0)
			{
				lock (_lock)
				{
					DestroySocket();
					disposed = true;
				}
			}
		}

		private void DestroySocket()
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
	}
}
