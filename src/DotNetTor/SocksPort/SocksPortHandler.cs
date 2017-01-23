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
    public sealed class SocksPortHandler : HttpMessageHandler
	{
	    private static volatile Socket _socket = null;
	    private static IPEndPoint _endPoint = null;

		// Tolerate errors
		private const int MaxRetry = 3;
		private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

		#region Constructors

		public SocksPortHandler(string address = "127.0.0.1", int socksPort = 9050)
			: this(new IPEndPoint(IPAddress.Parse(address), socksPort))
		{

		}

		public SocksPortHandler(IPEndPoint endpoint)
		{
			if (_endPoint == null)
				_endPoint = endpoint;
			else if(!Equals(_endPoint.Address, endpoint.Address) || !Equals(_endPoint.Port, endpoint.Port))
			{
				throw new TorException($"Cannot change {nameof(endpoint)}, until every {nameof(SocksPortHandler)}, is disposed. " +
										$"The current {nameof(endpoint)} is {_endPoint.Address}:{_endPoint.Port}, your desired is {endpoint.Address}:{endpoint.Port}");
			}

			// Subscribe to the list of those who need the static, shared socket
			// This will make sure it won't dispose or disconnect the tor socket, while others are using it
			// true: if needs the socket
			HandlersNeedSocket.AddOrUpdate(GetHashCode(), true, (k, v) => true);
		}

		#endregion

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Util.Semaphore.WaitOne();
			//await Util.Semaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				return await Task.Run(()=>
					Retry.Do(() => Send(request), RetryInterval, MaxRetry)
					).ConfigureAwait(false);
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

		private static HttpResponseMessage Send(HttpRequestMessage request)
		{

			Uri strippedUri = Util.StripPath(request.RequestUri);

			try
			{
				Retry.Do(ConnectToTorIfNotConnected, RetryInterval, MaxRetry);
			}
			catch (Exception ex)
			{
				throw new TorException("Failed to connect to TOR", ex);
			}

			try
			{
				Retry.Do(() => ConnectToDestinationIfNotConnected(strippedUri), RetryInterval, MaxRetry);
			}
			catch (Exception ex)
			{
				throw new TorException("Failed to connect to the destination", ex);
			}

			Util.ValidateRequest(request);
			SendRequest(request);

			HttpResponseMessage message = ReceiveResponse(request);

			return message;
		}

		private static volatile Stream _currentStream;

		private static void SendRequest(HttpRequestMessage request)
		{
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
				else requestHead += "Transfer-Encoding: chunked\r\n";

				// write all content headers
				requestHead +=
					request.Content.Headers.Where(header => !string.Equals(header.Key, "Transfer-Encoding", StringComparison.Ordinal))
						.Where(header => !string.Equals(header.Key, "Content-Length", StringComparison.Ordinal))
						.Where(header => !string.Equals(header.Key, "Host", StringComparison.Ordinal))
						.Aggregate(requestHead, (current, header) => current + ParseHeaderToString(header));
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
			_currentStream.Write(Encoding.UTF8.GetBytes(headAndContent), 0, headAndContent.Length);
			_currentStream.Flush();
		}

		private static string ParseHeaderToString(KeyValuePair<string, IEnumerable<string>> header)
			=> $"{header.Key}: " +
				$"{string.Join(",", header.Value)}" +
				"\r\n";

		private static HttpResponseMessage ReceiveResponse(HttpRequestMessage request)
		{
			using (var reader = new ByteStreamReader(_currentStream, _socket.ReceiveBufferSize, preserveLineEndings: false))
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
					HttpContent content = null;
					if (response.Headers.TransferEncodingChunked.GetValueOrDefault(false))
					{
						// read the body with chunked transfer encoding
						var chunkedStream = new ReadsFromChunksStream(reader.RemainingStream);
						content = new StreamContent(chunkedStream);
					}
					else if (response.Content.Headers.ContentLength.HasValue)
					{
						// read the body with a content-length
						var limitedStream = new LimitedStream(
							reader.RemainingStream,
							response.Content.Headers.ContentLength.Value);
						content = new StreamContent(limitedStream);
					}

					if (content != null)
					{
						// copy over the content headers
						foreach (var header in response.Content.Headers)
							content.Headers.TryAddWithoutValidation(header.Key, header.Value);

						response.Content = content;
					}
				}

				return response;
			}
		}

		#region DestinationConnections

		private static readonly ConcurrentDictionary<Uri, Stream> Connections = new ConcurrentDictionary<Uri, Stream>();

		private static bool IsConnectedToDestination(Uri uri)
		{
			if (!IsSocketConnected(throws: false))
				DestroyConnections();
			var strippedUri = Util.StripPath(uri);
			return Connections.ContainsKey(strippedUri);
		}

		private static void DestroyConnections()
		{
			try
			{
				foreach (Stream stream in Connections.Values)
					stream.Dispose();

				Connections.Clear();
			}
			catch
			{
				// ignored
			}
		}

		private static void ConnectToDestinationIfNotConnected(Uri strippedUri)
		{
			if (!IsConnectedToDestination(strippedUri))
			{
				ConnectToDestination(strippedUri);
			}
			else
			{
				Stream stream;
				Connections.TryGetValue(strippedUri, out stream);
				_currentStream = stream;
			}
		}

		private static void ConnectToDestination(Uri strippedUri)
		{
			var sendBuffer = Util.BuildConnectToUri(strippedUri).Array;
			_socket.Send(sendBuffer, SocketFlags.None);

			var recBuffer = new byte[_socket.ReceiveBufferSize];
			var recCnt = _socket.Receive(recBuffer, SocketFlags.None);

			Util.ValidateConnectToDestinationResponse(recBuffer, recCnt);

			Stream stream = new NetworkStream(_socket, ownsSocket: false);
			if (strippedUri.Scheme.Equals("https", StringComparison.Ordinal))
			{
				var httpsStream = new SslStream(stream, leaveInnerStreamOpen: true);

				httpsStream
					.AuthenticateAsClientAsync(
						strippedUri.DnsSafeHost,
						new X509CertificateCollection(),
						SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
						checkCertificateRevocation: false)
					.Wait();
				stream = httpsStream;
			}
			Connections.AddOrUpdate(strippedUri, stream, (k, v) => stream);
			_currentStream = stream;
		}

		#endregion

		#region TorConnection

		private static bool IsSocketConnected(bool throws)
		{
			try
			{
				if (_socket == null) return false;
				if (!_socket.Connected) return false;
				if (_socket.Available == 0) return false;
				if (_socket.Poll(1000, SelectMode.SelectRead)) return false;

				return true;
			}
			catch
			{
				if (throws) throw;

				return false;
			}
		}

		private static void ConnectToTorIfNotConnected()
		{
			if (!IsSocketConnected(throws: false)) // Socket.Connected is misleading, don't use that
			{
				ConnectSocket();
				HandshakeTor();
			}
		}

		private static void HandshakeTor()
		{
			var sendBuffer = new byte[] { 5, 1, 0 };
			_socket.Send(sendBuffer, SocketFlags.None);

			var recBuffer = new byte[_socket.ReceiveBufferSize];
			var recCnt = _socket.Receive(recBuffer, SocketFlags.None);

			Util.ValidateHandshakeResponse(recBuffer, recCnt);
		}

		private static void ConnectSocket()
		{
			DestroySocket(throws: false);
			_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
			{ Blocking = true};
			_socket.Connect(_endPoint);
		}

		#endregion

		#region Cleanup

		// int: hash of the handler, bool: true if needs the socket
		private static readonly ConcurrentDictionary<int, bool> HandlersNeedSocket = new ConcurrentDictionary<int, bool>();
		private void ReleaseUnmanagedResources()
		{
			// I don't need the socket anymore
			HandlersNeedSocket.AddOrUpdate(GetHashCode(), false, (k, v) => false);

			// If anyone needs the socket don't dispose it
			if (!HandlersNeedSocket.Values.Any(x => x))
			{
				DestroySocket(throws: false);

				HandlersNeedSocket.Clear();
			}
		}
		private static void DestroySocket(bool throws)
		{
			try
			{
				DestroyConnections();
				if (_socket.Connected)
					_socket.Shutdown(SocketShutdown.Both);
				_socket.Dispose();
			}
			catch
			{
				if (throws) throw;
			}
		}

		private volatile bool _disposed = false;
		protected override void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				Util.Semaphore.WaitOne();
				//Util.Semaphore.Wait();
				try
				{
					ReleaseUnmanagedResources();
				}
				catch (Exception)
				{
					// ignored
				}
				finally
				{
					Util.Semaphore.Release();
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
