using DotNetTor.SocksPort.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace DotNetTor.SocksPort.Net
{
	internal static class HttpSocketClient
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

		public static HttpResponseMessage ReceiveResponse(Stream stream, HttpRequestMessage request)
		{
			using (var reader = new ByteStreamReader(stream, BufferSize, preserveLineEndings: false))
			{
				HttpResponseMessage response = ReadResponseHead(reader, request);

				if (!MethodsWithoutResponseBody.Contains(request.Method))
				{
					ReadResponseBody(reader, response);
				}

				return response;
			}
		}

		private static void ReadResponseBody(ByteStreamReader reader, HttpResponseMessage response)
		{
			HttpContent content = null;
			if (response.Headers.TransferEncodingChunked.GetValueOrDefault(false))
			{
				// read the body with chunked transfer encoding
				Stream remainingStream = reader.RemainingStream;
				var chunkedStream = new ReadsFromChunksStream(remainingStream);
				content = new StreamContent(chunkedStream);
			}
			else if (response.Content.Headers.ContentLength.HasValue)
			{
				// read the body with a content-length
				Stream remainingStream = reader.RemainingStream;
				var limitedStream = new LimitedStream(remainingStream, response.Content.Headers.ContentLength.Value);
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

		private static HttpResponseMessage ReadResponseHead(ByteStreamReader reader, HttpRequestMessage request)
		{
			// initialize the response
			var response = new HttpResponseMessage { RequestMessage = request };

			// read the first line of the response
			string line = reader.ReadLine();
			var pieces = line.Split(new[] { ' ' }, 3);
			if (pieces[0] != "HTTP/1.1")
			{
				throw new HttpRequestException("The HTTP version the response is not supported.");
			}

			response.StatusCode = (HttpStatusCode)int.Parse(pieces[1]);
			response.ReasonPhrase = pieces[2];

			// read the headers
			response.Content = new ByteArrayContent(new byte[0]);
			while ((line = reader.ReadLine()) != null && line != string.Empty)
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

		public static void SendRequest(Stream networkStream, HttpRequestMessage request)
		{
			Util.ValidateRequest(request);

			using (var writer = new StreamWriter(networkStream, new UTF8Encoding(false, true), BufferSize, leaveOpen: true))
			{
				Stream contentStream = SendRequestHead(writer, request);

				if (contentStream != null)
				{
					contentStream.CopyTo(networkStream);
					networkStream.Flush();
				}
			}
		}

		private static Stream SendRequestHead(TextWriter writer, HttpRequestMessage request)
		{
			var location = request.Method != ConnectMethod ? request.RequestUri.PathAndQuery : $"{request.RequestUri.DnsSafeHost}:{request.RequestUri.Port}";
			writer.Write($"{request.Method.Method} {location} HTTP/{request.Version}" + LineSeparator);

			if (!MethodsWithoutHostHeader.Contains(request.Method))
			{
				string host = request.Headers.Contains(HostHeader) ? request.Headers.Host : request.RequestUri.Host;
				WriteHeader(writer, HostHeader, host);
			}

			Stream contentStream = null;
			if (request.Content != null && !MethodsWithoutRequestBody.Contains(request.Method))
			{
				contentStream = request.Content.ReadAsStreamAsync().Result;

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
					WriteHeader(writer, ContentLengthHeader, contentLength.ToString());
				}
				else
				{
					WriteHeader(writer, TransferEncodingHeader, "chunked");
				}

				// write all content headers
				foreach (var header in request.Content.Headers.Where(p => !SpecialHeaders.Contains(p.Key)))
				{
					WriteHeader(writer, header);
				}
			}

			// writer the rest of the request headers
			foreach (var header in request.Headers.Where(p => !SpecialHeaders.Contains(p.Key)))
			{
				WriteHeader(writer, header);
			}

			writer.Write(LineSeparator);
			writer.Flush();

			return contentStream;
		}

		private static void WriteHeader(TextWriter writer, string key, string value)
		{
			WriteHeader(writer, new KeyValuePair<string, IEnumerable<string>>(key, new[] {value}));
		}

		private static void WriteHeader(TextWriter writer, KeyValuePair<string, IEnumerable<string>> header)
		{
			writer.Write(
				$"{header.Key}: " +
				$"{string.Join(",", header.Value)}" +
				LineSeparator);
		}
	}
}