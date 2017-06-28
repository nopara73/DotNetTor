using System.Net.Http.Headers;

namespace DotNetTor.Http
{
    public struct HttpResponseContentHeaders
	{
		public HttpResponseHeaders ResponseHeaders { get; set; }
		public HttpContentHeaders ContentHeaders { get; set; }
	}
}
