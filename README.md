# DotNetTor
Library implementation of essential TOR features in .NET Core.

## See detailed documentation on [CodeProject](https://www.codeproject.com/script/Articles/ArticleVersion.aspx?waid=225577&aid=1161078)
  
##[Nuget](https://www.nuget.org/packages/DotNetTor)

##Build & Test

1. `git clone https://github.com/nopara73/DotNetTor`
2. `cd DotNetTor/`
3. `dotnet restore`
4. `cd src/DotNetTor.Tests/`
5. Configure TOR properly.
6. `dotnet test`

##Configure TOR
1. Download TOR Expert Bundle: https://www.torproject.org/download/download
2. Download the torrc config file sample: https://github.com/nopara73/DotNetTor/blob/master/torrc
3. Place torrc in the proper default location (depending on your OS) and edit it:
  - Optionally uncomment and edit the SocksPort, if you don't uncomment it will default to 9050 port anyway
  - The default ControlPort in the sample is 9051, optionally edit it.
  - Modify the password hash
    - To run my tests, for the `ControlPortPassword = "ILoveBitcoin21"` the hash should be: `HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6`
    - For your application you should use different one, a more complicated one. Then start tor like this: `tor --hash-password password`
      where password is the `password` you've chosen. It will give you the `HashedControlPassword`.
4. Start tor, it will listen to the ports you set in the config file.

##Usage

```cs
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
```

While the general wisdom is to use your `HttpClient`, instead of with `using` blocks, this API cannot handle yet requests to diffenet domains with the same handler, so don't do this:

```
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
```

If you want to reuse a handler pay attention to the an HttpClient's default behaviour. It will dispose the handler for you if you don't say to it otherwise.  

```
var httpClient = new HttpClient(handler, disposeHandler: false)
```


##Acknowledgement
Originally the SocksPort part of this project was a leaned down, modified and .NET Core ported version of the [SocketToMe](https://github.com/joelverhagen/SocketToMe) project.  
Originally the ControlPort part of this project was a leaned down, modified and .NET Core ported version of the [Tor.NET](https://github.com/sharpbrowser/Tor.NET) project.  
At this point of the development you can still find parts of the former project in the codebase, however the latter has been completely replaced.  
