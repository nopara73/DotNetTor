using DotNetTor.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static DotNetTor.Http.Constants;

namespace System.Net.Http
{
	public static class HttpMessageHelper
    {
		public static async Task<string> ReadStartLineAsync(StreamReader reader)
		{
			// https://tools.ietf.org/html/rfc7230#section-3
			// A recipient MUST parse an HTTP message as a sequence of octets in an
			// encoding that is a superset of US-ASCII[USASCII].

			// Read until the first CRLF
			// the CRLF is part of the startLine
			// https://tools.ietf.org/html/rfc7230#section-3.5
			// Although the line terminator for the start-line and header fields is
			// the sequence CRLF, a recipient MAY recognize a single LF as a line
			// terminator and ignore any preceding CR.
			var startLine = await reader.ReadPartAsync(char.Parse(LF)).ConfigureAwait(false) + LF;
			if (startLine == null || startLine == "") throw new FormatException($"{nameof(startLine)} cannot be null or empty");
			return startLine;
		}

		public static async Task<string> ReadHeadersAsync(StreamReader reader)
		{
			var headers = "";
			var firstRead = true;
			while (true)
			{
				var header = await reader.ReadLineAsync(strictCRLF: true).ConfigureAwait(false);
				if (header == null) throw new FormatException($"Malformed HTTP message: End of headers must be CRLF");
				if (header == "")
				{
					// 2 CRLF was read in row so it's the end of the headers
					break;
				}

				if (firstRead)
				{
					// https://tools.ietf.org/html/rfc7230#section-3
					// A recipient that receives whitespace between the
					// start - line and the first header field MUST either reject the message
					// as invalid or consume each whitespace-preceded line without further
					// processing of it(i.e., ignore the entire line, along with any				 
					// subsequent lines preceded by whitespace, until a properly formed				 
					// header field is received or the header section is terminated).
					if (Char.IsWhiteSpace(header[0]))
					{
						throw new FormatException($"Invalid HTTP message: Cannot be whitespace between the start line and the headers");
					}
					firstRead = false;
				}

				headers += header + CRLF; // CRLF is part of the headerstring
			}
			if (headers == null || headers == "")
			{
				headers = "";
			}

			return headers;
		}

		public static async Task<HttpContent> GetContentAsync(StreamReader reader, HttpRequestContentHeaders headerStruct)
		{
			if (headerStruct.RequestHeaders != null && headerStruct.RequestHeaders.Contains("Transfer-Encoding"))
			{
				if (headerStruct.RequestHeaders.TransferEncoding.Last().Value == "chunked")
				{
					throw new NotImplementedException();
				}
				// https://tools.ietf.org/html/rfc7230#section-3.3.3
				// If a Transfer - Encoding header field is present in a response and
				// the chunked transfer coding is not the final encoding, the
				// message body length is determined by reading the connection until
				// it is closed by the server.  If a Transfer - Encoding header field
				// is present in a request and the chunked transfer coding is not
				// the final encoding, the message body length cannot be determined
				// reliably; the server MUST respond with the 400(Bad Request)
				// status code and then close the connection.
				else
				{
					return await GetContentTillEndAsync(reader).ConfigureAwait(false);
				}
			}
			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 5.If a valid Content - Length header field is present without
			// Transfer - Encoding, its decimal value defines the expected message
			// body length in octets.If the sender closes the connection or
			// the recipient times out before the indicated number of octets are
			// received, the recipient MUST consider the message to be
			// incomplete and close the connection.
			else if (headerStruct.ContentHeaders.Contains("Content-Length"))
			{
				long? contentLength = headerStruct.ContentHeaders?.ContentLength;
				return await GetContentTillLengthAsync(reader, contentLength).ConfigureAwait(false);
			}

			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 6.If this is a request message and none of the above are true, then
			// the message body length is zero (no message body is present).
			// 7.  Otherwise, this is a response message without a declared message
			// body length, so the message body length is determined by the
			// number of octets received prior to the server closing the
			// connection.
			return GetDummyOrNullContent(headerStruct.ContentHeaders);
		}		

		public static async Task<HttpContent> GetContentAsync(StreamReader reader, HttpResponseContentHeaders headerStruct, HttpMethod requestMethod, StatusLine statusLine)
		{
			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// The length of a message body is determined by one of the following
			// (in order of precedence):
			// 1.Any response to a HEAD request and any response with a 1xx
			// (Informational), 204(No Content), or 304(Not Modified) status
			// code is always terminated by the first empty line after the
			// header fields, regardless of the header fields present in the
			// message, and thus cannot contain a message body.
			if (requestMethod == HttpMethod.Head
				|| HttpStatusCodeHelper.IsInformational(statusLine.StatusCode)
				|| statusLine.StatusCode == HttpStatusCode.NoContent
				|| statusLine.StatusCode == HttpStatusCode.NotModified)
			{
				return GetDummyOrNullContent(headerStruct.ContentHeaders);
			}
			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 2.Any 2xx(Successful) response to a CONNECT request implies that
			// the connection will become a tunnel immediately after the empty
			// line that concludes the header fields.A client MUST ignore any
			// Content - Length or Transfer-Encoding header fields received in
			// such a message.
			else if (requestMethod == new HttpMethod("CONNECT"))
			{
				if (HttpStatusCodeHelper.IsSuccessful(statusLine.StatusCode))
				{
					return null;
				}
			}
			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 3.If a Transfer-Encoding header field is present and the chunked
			// transfer coding(Section 4.1) is the final encoding, the message
			// body length is determined by reading and decoding the chunked
			// data until the transfer coding indicates the data is complete.
			if (headerStruct.ResponseHeaders != null && headerStruct.ResponseHeaders.Contains("Transfer-Encoding"))
			{
				if (headerStruct.ResponseHeaders.TransferEncoding.Last().Value == "chunked")
				{
					throw new NotImplementedException();
				}
				// https://tools.ietf.org/html/rfc7230#section-3.3.3
				// If a Transfer - Encoding header field is present in a response and
				// the chunked transfer coding is not the final encoding, the
				// message body length is determined by reading the connection until
				// it is closed by the server.  If a Transfer - Encoding header field
				// is present in a request and the chunked transfer coding is not
				// the final encoding, the message body length cannot be determined
				// reliably; the server MUST respond with the 400(Bad Request)
				// status code and then close the connection.
				else
				{
					return await GetContentTillEndAsync(reader).ConfigureAwait(false);
				}
			}
			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 5.If a valid Content - Length header field is present without
			// Transfer - Encoding, its decimal value defines the expected message
			// body length in octets.If the sender closes the connection or
			// the recipient times out before the indicated number of octets are
			// received, the recipient MUST consider the message to be
			// incomplete and close the connection.
			else if (headerStruct.ContentHeaders.Contains("Content-Length"))
			{
				long? contentLength = headerStruct.ContentHeaders?.ContentLength;

				return await GetContentTillLengthAsync(reader, contentLength).ConfigureAwait(false);
			}

			// https://tools.ietf.org/html/rfc7230#section-3.3.3
			// 6.If this is a request message and none of the above are true, then
			// the message body length is zero (no message body is present).
			// 7.  Otherwise, this is a response message without a declared message
			// body length, so the message body length is determined by the
			// number of octets received prior to the server closing the
			// connection.
			return await GetContentTillEndAsync(reader).ConfigureAwait(false);
		}

		private static async Task<HttpContent> GetContentTillEndAsync(StreamReader reader)
		{
			var contentString = await reader.ReadToEndAsync().ConfigureAwait(false);
			var contentBytes = reader.CurrentEncoding.GetBytes(contentString);
			return new ByteArrayContent(contentBytes);
		}

		private static async Task<HttpContent> GetContentTillLengthAsync(StreamReader reader, long? contentLength)
		{
			var buffer = new char[(long)contentLength];
			var left = contentLength;
			while (left != 0)
			{
				// TODO: don't just cast to int, handle overflow!
				var c = await reader.ReadAsync(buffer, 0, (int)left).ConfigureAwait(false);
				left -= c;
			}
			return new ByteArrayContent(reader.CurrentEncoding.GetBytes(buffer));
		}

		public static void AssertValidResponse(HttpHeaders messageHeaders, HttpContentHeaders contentHeaders)
		{
			if (messageHeaders != null && messageHeaders.Contains("Transfer-Encoding"))
			{
				if (contentHeaders != null && contentHeaders.Contains("Content-Length"))
				{
					contentHeaders.Remove("Content-Length");
				}
			}
			// Any Content-Length field value greater than or equal to zero is valid.
			if (contentHeaders.Contains("Content-Length"))
			{
				if (contentHeaders.ContentLength < 0)
					throw new HttpRequestException("Content-Length MUST be bigger than zero");
			}
		}

		public static HttpContent GetDummyOrNullContent(HttpContentHeaders contentHeaders)
		{
			if (contentHeaders != null && contentHeaders.Count() != 0)
			{
				return new ByteArrayContent(new byte[] { }); // dummy empty content
			}
			else
			{
				return null;
			}
		}

		public static void CopyHeaders(HttpHeaders source, HttpHeaders destination)
		{
			if (source != null && source.Count() != 0)
			{
				foreach (var header in source)
				{
					destination.TryAddWithoutValidation(header.Key, header.Value);
				}
			}
		}
	}
}
