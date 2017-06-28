using System.Net.Http.Headers;

namespace DotNetTor.Http
{
	public struct HttpRequestContentHeaders
	{
		public HttpRequestHeaders RequestHeaders { get; set; }
		public HttpContentHeaders ContentHeaders { get; set; }
	}
}
