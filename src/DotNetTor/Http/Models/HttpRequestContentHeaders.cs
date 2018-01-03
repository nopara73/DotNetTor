using System.Net.Http.Headers;

namespace DotNetTor.Http.Models
{
	public class HttpRequestContentHeaders
	{
		public HttpRequestHeaders RequestHeaders { get; set; }
		public HttpContentHeaders ContentHeaders { get; set; }
	}
}
