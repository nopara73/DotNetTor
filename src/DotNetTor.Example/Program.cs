using DotNetTor.SocksPort.Net;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetTor.SocksPort;
using NBitcoin;
using QBitNinja.Client;
using QBitNinja.Client.Models;

namespace DotNetTor.Example
{
	public class Program
	{
		// For proper configuraion see https://github.com/nopara73/DotNetTor
		private static void Main()
		{
			//DoARandomRequest();
			//RequestWith3Ip();
			//CantRequestDifferentDomainsWithSameHandler();
			//PayAttentionToHttpClientDisposesHandler();

			FooAsync().Wait();
			//BarAsync().Wait();

			Console.WriteLine("Press a key to exit..");
			Console.ReadKey();
		}

		private static async Task BarAsync()
		{
			QBitNinjaClient client = new QBitNinjaClient(Network.Main);
			client.SetHttpMessageHandler(new SocksPortHandler());

			var tasks = new HashSet<Task<BalanceModel>>();
			var addresses = new HashSet<BitcoinAddress>();
			for (var i = 0; i < 10; i++)
			{
				addresses.Add(new Key().GetBitcoinSecret(Network.Main).GetAddress());
			}

			foreach (var dest in addresses)
			{
				var task = client.GetBalance(dest, false);
				tasks.Add(task);
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);

			var results = new HashSet<BalanceModel>();
			foreach (var task in tasks)
				results.Add(await task.ConfigureAwait(false));
			foreach (var res in results)
				Console.WriteLine(res);
		}

		private static async Task FooAsync()
		{
			using (var httpClient = new HttpClient(new SocksPortHandler()))
			{
				var tasks = new HashSet<Task<HttpResponseMessage>>();
				for (var i= 0; i < 10; i++)
				{
					var task = httpClient.GetAsync("https://api.qbit.ninja/whatisit/what%20is%20my%20future");
					tasks.Add(task);
				}
				try
				{
					await Task.WhenAll(tasks).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
				}

				var results = new HashSet<HttpResponseMessage>();
				foreach (var task in tasks)
					results.Add(await task.ConfigureAwait(false));
				foreach (var res in results)
					Console.WriteLine(res.Content.ReadAsStringAsync().Result);
			}
		}

		private static void PayAttentionToHttpClientDisposesHandler()
		{
			var handler = new SocksPortHandler("127.0.0.1", 9050);
			using (var httpClient = new HttpClient(handler, disposeHandler: false))
			{
				HttpResponseMessage message = httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine(content);
			}
			using (var httpClient = new HttpClient(handler))
			{
				HttpResponseMessage message = httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").Result;
				var content = message.Content.ReadAsStringAsync().Result;
				Console.WriteLine(content);
			}
			try
			{
				using (var httpClient = new HttpClient(handler))
				{
					HttpResponseMessage message = httpClient.GetAsync("http://api.qbit.ninja/whatisit/what%20is%20my%20future").Result;
					var content = message.Content.ReadAsStringAsync().Result;
					Console.WriteLine(content);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Don't do this!");
				Console.WriteLine(ex.Message);
			}
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
		private static void CantRequestDifferentDomainsWithSameHandler()
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
				catch (AggregateException ex) when (ex.InnerException != null && ex.InnerException is TorException)
				{
					Console.WriteLine("Don't do this!");
					Console.WriteLine(ex.InnerException.Message);
				}
			}
		}
	}
}