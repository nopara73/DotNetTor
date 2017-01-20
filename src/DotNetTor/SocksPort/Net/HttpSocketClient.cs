using DotNetTor.SocksPort.Helpers;
using System;
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

namespace DotNetTor.SocksPort.Net
{
	internal class HttpSocketClient
	{
		private const int BufferSize = 4096;
		private const string HostHeader = "Host";
		private const string ContentLengthHeader = "Content-Length";
		private const string TransferEncodingHeader = "Transfer-Encoding";
		private const string LineSeparator = "\r\n";

		private static readonly HttpMethod ConnectMethod = new HttpMethod("CONNECT");
		private static readonly ISet<HttpMethod> MethodsWithoutHostHeader = new HashSet<HttpMethod> { ConnectMethod };
		private static readonly ISet<HttpMethod> MethodsWithoutRequestBody = new HashSet<HttpMethod> { ConnectMethod, HttpMethod.Head };
		private static readonly ISet<HttpMethod> MethodsWithoutResponseBody = new HashSet<HttpMethod> { ConnectMethod, HttpMethod.Head };
		private static readonly ISet<string> SpecialHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HostHeader, ContentLengthHeader, TransferEncodingHeader };
		
		public async Task<HttpResponseMessage> ReceiveResponseAsync(Stream stream, HttpRequestMessage request)
		{
			var reader = new ByteStreamReader(stream, BufferSize, false);
			
			var response = await ReadResponseHeadAsync(reader, request).ConfigureAwait(false);
				
			if (!MethodsWithoutResponseBody.Contains(request.Method))
			{
				ReadResponseBody(reader, response);
			}

			return response;
		}

		private void ReadResponseBody(ByteStreamReader reader, HttpResponseMessage response)
		{
			HttpContent content = null;
			if (response.Headers.TransferEncodingChunked.GetValueOrDefault(false))
			{
				// read the body with chunked transfer encoding
				var remainingStream = reader.GetRemainingStream();
				var chunkedStream = new ReadsFromChunksStream(remainingStream);
				content = new StreamContent(chunkedStream);
			}
			else if (response.Content.Headers.ContentLength.HasValue)
			{
				// read the body with a content-length
				var remainingStream = reader.GetRemainingStream();
				var limitedStream = new LimitedStream(remainingStream, response.Content.Headers.ContentLength.Value, true);
				content = new StreamContent(limitedStream);
			}
			else
			{
				// TODO: should we immediately close the connection in this case?
			}

			if (content != null)
			{
				// copy over the content headers
				foreach (var header in response.Content.Headers)
				{
					content.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}

				response.Content = content;
			}
		}

		private async Task<HttpResponseMessage> ReadResponseHeadAsync(ByteStreamReader reader, HttpRequestMessage request)
		{
			// initialize the response
			var response = new HttpResponseMessage { RequestMessage = request };

			// read the first line of the response
			string line = await reader.ReadLineAsync().ConfigureAwait(false);
			string[] pieces = line.Split(new[] { ' ' }, 3);
			if (pieces[0] != "HTTP/1.1")
			{
				throw new HttpRequestException("The HTTP version the response is not supported.");
			}

			response.StatusCode = (HttpStatusCode)int.Parse(pieces[1]);
			response.ReasonPhrase = pieces[2];

			// read the headers
			response.Content = new ByteArrayContent(new byte[0]);
			while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null && line != string.Empty)
			{
				pieces = line.Split(new[] { ":" }, 2, StringSplitOptions.None);
				if (pieces[1].StartsWith(" ", StringComparison.Ordinal))
				{
					pieces[1] = pieces[1].Substring(1);
				}

				if (!response.Headers.TryAddWithoutValidation(pieces[0], pieces[1]))
				{
					if (!response.Content.Headers.TryAddWithoutValidation(pieces[0], pieces[1]))
					{
						throw new InvalidOperationException($"The header '{pieces[0]}' could not be added to the response message or to the response content.");
					}
				}
			}

			return response;
		}

		public async Task SendRequestAsync(Stream networkStream, HttpRequestMessage request)
		{
			ValidateRequest(request);

			Stream contentStream;
			using (var writer = new StreamWriter(networkStream, new UTF8Encoding(false, true), BufferSize, true))
			{
				contentStream = await SendRequestHeadAsync(writer, request).ConfigureAwait(false);
			}

			if (contentStream != null)
			{
				await contentStream.CopyToAsync(networkStream).ConfigureAwait(false);
				await networkStream.FlushAsync().ConfigureAwait(false);
			}
		}

		private async Task<Stream> SendRequestHeadAsync(StreamWriter writer, HttpRequestMessage request)
		{
			var location = request.Method != ConnectMethod ? request.RequestUri.PathAndQuery : $"{request.RequestUri.DnsSafeHost}:{request.RequestUri.Port}";
			await writer.WriteAsync($"{request.Method.Method} {location} HTTP/{request.Version}" + LineSeparator).ConfigureAwait(false);

			if (!MethodsWithoutHostHeader.Contains(request.Method))
			{
				string host = request.Headers.Contains(HostHeader) ? request.Headers.Host : request.RequestUri.Host;
				await WriteHeaderAsync(writer, HostHeader, host).ConfigureAwait(false);
			}

			Stream contentStream = null;
			if (request.Content != null && !MethodsWithoutRequestBody.Contains(request.Method))
			{
				contentStream = await request.Content.ReadAsStreamAsync().ConfigureAwait(false);

				// determine whether to use chunked transfer encoding
				long? contentLength = null;
				if (!request.Headers.TransferEncodingChunked.GetValueOrDefault(false))
				{
					try
					{
						contentLength = contentStream.Length;
					}
					catch (Exception)
					{
						// we cannot get the request content length, so fall back to chunking
					}
				}

				// set the appropriate content transfer headers
				if (contentLength.HasValue)
				{
					// TODO: we are preferring the content length provided by the caller... is this right?
					contentLength = request.Content.Headers.ContentLength ?? contentLength;
					await WriteHeaderAsync(writer, ContentLengthHeader, contentLength.ToString()).ConfigureAwait(false);
				}
				else
				{
					await WriteHeaderAsync(writer, TransferEncodingHeader, "chunked").ConfigureAwait(false);
				}

				// write all content headers
				foreach (var header in request.Content.Headers.Where(p => !SpecialHeaders.Contains(p.Key)))
				{
					await WriteHeaderAsync(writer, header).ConfigureAwait(false);
				}
			}

			// writer the rest of the request headers
			foreach (var header in request.Headers.Where(p => !SpecialHeaders.Contains(p.Key)))
			{
				await WriteHeaderAsync(writer, header).ConfigureAwait(false);
			}

			await writer.WriteAsync(LineSeparator).ConfigureAwait(false);
			await writer.FlushAsync().ConfigureAwait(false);

			return contentStream;
		}

		private async Task WriteHeaderAsync(TextWriter writer, string key, string value)
		{
			await WriteHeaderAsync(writer, new KeyValuePair<string, IEnumerable<string>>(key, new[] { value })).ConfigureAwait(false);
		}

		private async Task WriteHeaderAsync(TextWriter writer, KeyValuePair<string, IEnumerable<string>> header)
		{
			await writer.WriteAsync($"{header.Key}: {string.Join(",", header.Value)}" + LineSeparator).ConfigureAwait(false);
		}

		private void ValidateRequest(HttpRequestMessage request)
		{
			if (request.RequestUri.Scheme.Equals("http", StringComparison.Ordinal))
			{
				
			}
			else if (request.RequestUri.Scheme.Equals("https", StringComparison.Ordinal))
			{
				
			}
			else throw new NotSupportedException("Only HTTP and HTTPS are supported.");

			if (request.Version != new Version(1, 1))
			{
				throw new NotSupportedException("Only HTTP/1.1 is supported.");
			}
		}
	}
}