using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetTor.SocksPort.Helpers;

namespace DotNetTor.SocksPort
{
	internal sealed class SocksConnection
	{
		public IPEndPoint EndPoint = null;
		public Uri Destination;
		public Socket Socket;
		public Stream Stream;
		public int ReferenceCount;
		public readonly object Lock = new object();

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

		private void ConnectToDestination()
		{
			var sendBuffer = Util.BuildConnectToUri(Destination).Array;
			Socket.Send(sendBuffer, SocketFlags.None);

			var recBuffer = new byte[Socket.ReceiveBufferSize];
			var recCnt = Socket.Receive(recBuffer, SocketFlags.None);

			Util.ValidateConnectToDestinationResponse(recBuffer, recCnt);

			Stream stream = new NetworkStream(Socket, ownsSocket: false);
			if (Destination.Scheme.Equals("https", StringComparison.Ordinal))
			{
				var httpsStream = new SslStream(stream, leaveInnerStreamOpen: true);

				httpsStream
					.AuthenticateAsClientAsync(
						Destination.DnsSafeHost,
						new X509CertificateCollection(),
						SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
						checkCertificateRevocation: false)
					.Wait();
				stream = httpsStream;
			}
			Stream = stream;
		}


		private static string ParseHeaderToString(KeyValuePair<string, IEnumerable<string>> header)
			=> $"{header.Key}: " +
				$"{string.Join(",", header.Value)}" +
				"\r\n";

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

		public HttpResponseMessage SendRequest(HttpRequestMessage request)
		{
			lock (Lock)
			{
				try
				{
					EnsureConnectedToTor();
					var isConnectRequest = Equals(request.Method, new HttpMethod("CONNECT"));
					string location;
					if (!isConnectRequest)
						location = request.RequestUri.PathAndQuery;
					else
						location = $"{request.RequestUri.DnsSafeHost}:{request.RequestUri.Port}";

					string requestHead = $"{request.Method.Method} {location} HTTP/{request.Version}\r\n";

					if (!isConnectRequest)
					{
						string host = request.Headers.Contains("Host") ? request.Headers.Host : request.RequestUri.Host;
						requestHead += $"Host: {host}\r\n";
					}

					string content = "";
					if (request.Content != null && !isConnectRequest && request.Method != HttpMethod.Head)
					{
						content = request.Content.ReadAsStringAsync().Result;

						// determine whether to use chunked transfer encoding
						long? contentLength = null;
						if (!request.Headers.TransferEncodingChunked.GetValueOrDefault(false))
							contentLength = content.Length;

						// set the appropriate content transfer headers
						if (contentLength.HasValue)
						{
							contentLength = request.Content.Headers.ContentLength ?? contentLength;
							requestHead += $"Content-Length: {contentLength}\r\n";
						}
						else
							requestHead += "Transfer-Encoding: chunked\r\n";

						//write all content headers
						string result = "";
						foreach (var header in request.Content.Headers)
						{
							if (!string.Equals(header.Key, "Transfer-Encoding", StringComparison.Ordinal))
							{
								if (!string.Equals(header.Key, "Content-Length", StringComparison.Ordinal))
								{
									if (!string.Equals(header.Key, "Host", StringComparison.Ordinal))
										result = ParseHeaderToString(header);
								}
							}
						}

						requestHead += result;
					}

					// write the rest of the request headers
					foreach (var header in request.Headers)
					{
						if (!string.Equals(header.Key, "Transfer-Encoding", StringComparison.Ordinal))
						{
							if (!string.Equals(header.Key, "Content-Length", StringComparison.Ordinal))
							{
								if (!string.Equals(header.Key, "Host", StringComparison.Ordinal))
									requestHead += ParseHeaderToString(header);
							}
						}
					}

					requestHead += "\r\n";

					var headAndContent = requestHead + content;
					Stream.Write(Encoding.UTF8.GetBytes(headAndContent), 0, headAndContent.Length);
					Stream.Flush();

					using (var reader = new ByteStreamReader(Stream, Socket.ReceiveBufferSize, preserveLineEndings: false))
					{
						// initialize the response
						var response = new HttpResponseMessage { RequestMessage = request };

						// read the first line of the response
						string line = reader.ReadLine();
						var pieces = line.Split(new[] { ' ' }, 3);

						if (!string.Equals(pieces[0], "HTTP/1.1", StringComparison.Ordinal))
							throw new HttpRequestException($"Only HTTP/1.1 is supported, actual: {pieces[0]}");

						response.StatusCode = (HttpStatusCode)int.Parse(pieces[1]);
						response.ReasonPhrase = pieces[2];

						// read the headers
						response.Content = new ByteArrayContent(new byte[0]);
						while ((line = reader.ReadLine()) != null && line != string.Empty)
						{
							pieces = line.Split(new[] { ":" }, 2, StringSplitOptions.None);
							if (pieces[1].StartsWith(" ", StringComparison.Ordinal))
								pieces[1] = pieces[1].Substring(1);

							if (!response.Headers.TryAddWithoutValidation(pieces[0], pieces[1]) &&
								!response.Content.Headers.TryAddWithoutValidation(pieces[0], pieces[1]))
								throw new InvalidOperationException(
									$"The header '{pieces[0]}' could not be added to the response message or to the response content.");
						}

						if (!(request.Method == new HttpMethod("CONNECT") || request.Method == HttpMethod.Head))
						{
							HttpContent httpContent = null;
							if (response.Headers.TransferEncodingChunked.GetValueOrDefault(false))
							{
								// read the body with chunked transfer encoding
								var chunkedStream = new ReadsFromChunksStream(reader.RemainingStream);
								httpContent = new StreamContent(chunkedStream);
							}
							else if (response.Content.Headers.ContentLength.HasValue)
							{
								// read the body with a content-length
								var limitedStream = new LimitedStream(
									reader.RemainingStream,
									response.Content.Headers.ContentLength.Value);
								httpContent = new StreamContent(limitedStream);
							}
							else return response;

							if (content != null)
							{
								// copy over the content headers
								foreach (var header in response.Content.Headers)
									httpContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

								response.Content = httpContent;
							}
						}

						return response;
					}
				}
				catch (SocketException)
				{
					DestroySocket();
					throw;
				}

			}
		}

		private void EnsureConnectedToTor()
		{
			if (!IsSocketConnected(throws: false)) // Socket.Connected is misleading, don't use that
			{
				DestroySocket();
				ConnectSocket();
				HandshakeTor();
				ConnectToDestination();
			}
		}

		public void AddReference() => Interlocked.Increment(ref ReferenceCount);

		public void RemoveReference(out bool disposed)
		{
			disposed = false;
			var value = Interlocked.Decrement(ref ReferenceCount);
			if (value == 0)
			{
				lock (Lock)
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

	public sealed class SocksPortHandler : HttpMessageHandler
	{

		// Tolerate errors
		private const int MaxRetry = 3;
		private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

		private static ConcurrentDictionary<string, SocksConnection> _Connections = new ConcurrentDictionary<string, SocksConnection>();

		#region Constructors

		public SocksPortHandler(string address = "127.0.0.1", int socksPort = 9050)
			: this(new IPEndPoint(IPAddress.Parse(address), socksPort))
		{

		}

		public SocksPortHandler(IPEndPoint endpoint)
		{
			if (EndPoint == null)
				EndPoint = endpoint;
			else if (!Equals(EndPoint.Address, endpoint.Address) || !Equals(EndPoint.Port, endpoint.Port))
			{
				throw new TorException($"Cannot change {nameof(endpoint)}, until every {nameof(SocksPortHandler)}, is disposed. " +
										$"The current {nameof(endpoint)} is {EndPoint.Address}:{EndPoint.Port}, your desired is {endpoint.Address}:{endpoint.Port}");
			}
		}


		public readonly IPEndPoint EndPoint = null;
		#endregion

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Util.Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				return Retry.Do(() => Send(request), RetryInterval, MaxRetry);
			}
			catch (Exception ex)
			{
				throw new TorException("Couldn't send the request", ex);
			}
			finally
			{
				Util.Semaphore.Release();
			}
		}

		private HttpResponseMessage Send(HttpRequestMessage request)
		{
			SocksConnection connection = null;
			try
			{
				Retry.Do(() =>
				{
					connection = ConnectToDestinationIfNotConnected(request.RequestUri);
				}, RetryInterval, MaxRetry);
			}
			catch (Exception ex)
			{
				throw new TorException("Failed to connect to the destination", ex);
			}

			Util.ValidateRequest(request);
			HttpResponseMessage message = connection.SendRequest(request);

			return message;
		}



		#region DestinationConnections

		private List<Uri> _References = new List<Uri>();
		private SocksConnection ConnectToDestinationIfNotConnected(Uri uri)
		{
			uri = Util.StripPath(uri);
			lock (_Connections)
			{
				SocksConnection connection;
				if (_Connections.TryGetValue(uri.AbsoluteUri, out connection))
				{
					if (!_References.Contains(uri))
					{
						connection.AddReference();
						_References.Add(uri);
					}
					return connection;
				}

				connection = new SocksConnection
				{
					EndPoint = EndPoint,
					Destination = uri
				};
				connection.AddReference();
				_References.Add(uri);
				_Connections.TryAdd(uri.AbsoluteUri, connection);
				return connection;
			}
		}

		#endregion

		#region TorConnection


		#endregion

		#region Cleanup

		private void ReleaseUnmanagedResources()
		{
			lock (_Connections)
			{
				foreach (var reference in _References)
				{
					SocksConnection connection = null;
					if (_Connections.TryGetValue(reference.AbsoluteUri, out connection))
					{
						bool disposedSockets;
						connection.RemoveReference(out disposedSockets);
						if (disposedSockets)
						{
							_Connections.TryRemove(reference.AbsoluteUri, out connection);
						}
					}
				}
			}
		}

		private volatile bool _disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				try
				{
					ReleaseUnmanagedResources();
				}
				catch (Exception)
				{
					// ignored
				}

				_disposed = true;
			}

			base.Dispose(disposing);
		}
		~SocksPortHandler()
		{
			Dispose(false);
		}

		#endregion
	}
}
