using System;
using System.Net.Http;
using DotNetTor.SocksPort;

namespace DotNetTor.Example
{
	public class Program
	{
		// For proper configuraion see https://github.com/nopara73/DotNetTor
		private static void Main()
		{
			DoARandomRequest();
			RequestWith3Ip();
			CanRequestDifferentDomainsWithSameHandler();

			Console.WriteLine("Press a key to exit..");
			Console.ReadKey();
		}

		private static void DoARandomRequest()
		{
			using (var httpClient = new HttpClient(new SocksPortHandler("127.0.0.1", 9050)))
			{
				HttpResponseMessage message = httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine(content);
			}
		}

		private static void RequestWith3Ip()
		{
			var requestUri = "http://icanhazip.com/";

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var message = httpClient.GetAsync(requestUri).Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your real IP: \t\t{content}");
			}

			// 2. Get TOR IP
			using (var httpClient = new HttpClient(new SocksPortHandler("127.0.0.1", socksPort: 9050)))
			{
				var message = httpClient.GetAsync(requestUri).Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your TOR IP: \t\t{content}");

				// 3. Change TOR IP
				var controlPortClient = new ControlPort.Client("127.0.0.1", controlPort: 9051, password: "ILoveBitcoin21");
				controlPortClient.ChangeCircuitAsync().Wait();

				// 4. Get changed TOR IP
				message = httpClient.GetAsync(requestUri).Result;
				content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your other TOR IP: \t{content}");
			}
		}
		private static void CanRequestDifferentDomainsWithSameHandler()
		{
			using (var httpClient = new HttpClient(new SocksPortHandler()))
			{
				var message = httpClient.GetAsync("http://icanhazip.com/").Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine($"Your TOR IP: \t\t{content}");

				try
				{
					message = httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").Result;
					content = message.Content.ReadAsStringAsync().Result;
					Console.WriteLine(content);
				}
				catch (AggregateException ex) when (ex.InnerException is TorException)
				{
					Console.WriteLine("Don't do this!");
					Console.WriteLine(ex.InnerException.Message);
				}
			}
		}
	}
}