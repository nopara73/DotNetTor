using System.Net.Http.Headers;

namespace DotNetTor.Http.Models
{
    public class HttpResponseContentHeaders
	{
		public HttpResponseHeaders ResponseHeaders { get; set; }
		public HttpContentHeaders ContentHeaders { get; set; }
	}
}
