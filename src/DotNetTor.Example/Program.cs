using System;
using System.Net.Http;

namespace DotNetTor.Example
{
    public class Program
    {
		// 1. Download TOR Expert Bundle: https://www.torproject.org/download/download
		// 2. Download the torrc config file sample: https://svn.torproject.org/svn/tor/tags/tor-0_0_9_5/src/config/torrc.sample.in
		// 3. Place torrc in the proper default location (depending on your OS) and edit it:
		//	- Uncomment the default ControlPort 9051
		//	- Uncomment and modify the password hash to HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6
		// 4. Run tor (it will run in the background and listen to the SocksPort 9050 and ControlPort 9051)
		// Now the example should successfully run
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
			using (var socksPortClient = new SocksPort.Client())
			{
				var handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					Console.WriteLine(content);
				}
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
			using (var socksPortClient = new SocksPort.Client())
			{
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
				handler = socksPortClient.GetHandlerFromRequestUri(requestUri);
				using (var httpClient = new HttpClient(handler))
				{
					var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
					Console.WriteLine($"Your other TOR IP: \t{content}");
				}
			}
		}
	}
}
