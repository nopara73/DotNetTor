using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotNetTor.Test
{
    public class Program
    {
        public static void Main(string[] args)
		{
			RequestWith3Ip();
			//DoSomeRandomRequest();


			Console.WriteLine("Press a key to exit..");
			Console.ReadKey();
		}

		private static void DoSomeRandomRequest()
		{
			var requestUri = "http://api.qbit.ninja/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json";
			var socksPortClient = new SocksPort.Client();
			var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
			using (var httpClient = new HttpClient(handler))
			{
				var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				Console.WriteLine(content);
			}
		}

		private static void RequestWith3Ip()
		{
			var requestUri = "http://icanhazip.com/";

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your real IP: \t\t{content}");
			}

			// 2. Get TOR IP
			var socksPortClient = new SocksPort.Client();
			var handler = socksPortClient.GetHandlerFromDomain("icanhazip.com");
			using (var httpClient = new HttpClient(handler))
			{
				var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your TOR IP: \t\t{ content}");
			}

			// 3. Change TOR IP
			var controlPortClient = new ControlPort.Client(password: "ILoveBitcoin21");
			controlPortClient.ChangeCircuit();

			// 4. Get changed TOR IP
			socksPortClient = new SocksPort.Client();
			handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
			using (var httpClient = new HttpClient(handler))
			{
				var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your other TOR IP: \t{content}");
			}
		}
	}
}
