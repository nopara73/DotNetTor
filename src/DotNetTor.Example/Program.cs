using System;
using System.Net.Http;
using DotNetTor.SocksPort;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace DotNetTor.Example
{
	public class Program
	{
		// For proper configuraion see https://github.com/nopara73/DotNetTor
#pragma warning disable IDE1006 // Naming Styles
		private static async Task Main()
#pragma warning restore IDE1006 // Naming Styles
		{
			await DoARandomRequestAsync();
			await RequestWith3IpAsync();
			await CanRequestDifferentDomainsWithSameHandlerAsync();

			Console.WriteLine("Press a key to exit..");
			Console.ReadKey();
		}

		private static async Task DoARandomRequestAsync()
		{
			using (var httpClient = new HttpClient(new SocksPortHandler("127.0.0.1", 9050)))
			{
				HttpResponseMessage message = await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future");
				var content = await message.Content.ReadAsStringAsync();
				Console.WriteLine(content);
			}
		}

		private static async Task RequestWith3IpAsync()
		{
			var requestUri = "https://api.ipify.org/";

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();
				Console.WriteLine($"Your real IP: \t\t{content}");
			}

			// 2. Get TOR IP
			using (var httpClient = new HttpClient(new SocksPortHandler("127.0.0.1", socksPort: 9050)))
			{
				var message = await httpClient.GetAsync(requestUri);
				var content = await message.Content.ReadAsStringAsync();
				Console.WriteLine($"Your TOR IP: \t\t{content}");

				// 3. Change TOR IP
				var controlPortClient = new ControlPort.TorControlClient("127.0.0.1", controlPort: 9051, password: "ILoveBitcoin21");
				await controlPortClient.ChangeCircuitAsync();

				// 4. Get changed TOR IP
				message = await httpClient.GetAsync(requestUri);
				content = await message.Content.ReadAsStringAsync();
				Console.WriteLine($"Your other TOR IP: \t{content}");
			}
		}
		private static async Task CanRequestDifferentDomainsWithSameHandlerAsync()
		{
			using (var httpClient = new HttpClient(new SocksPortHandler()))
			{
				var message = await httpClient.GetAsync("https://api.ipify.org/");
				var content = await message.Content.ReadAsStringAsync();
				Console.WriteLine($"Your TOR IP: \t\t{content}");

				try
				{
					message = await httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future");
					content = await message.Content.ReadAsStringAsync();
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