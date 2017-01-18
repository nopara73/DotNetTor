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
var requestUri = "http://icanhazip.com/"; // Gives back your IP

// 1. Get your (real) IP, just like you normally do
using (var httpClient = new HttpClient())
{
	var content = httpClient.GetAsync(requestUri).Result.Content.ReadAsStringAsync().Result;
	Console.WriteLine($"Your real IP: \t\t{content}");
}

// 2. Get your IP through TOR
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
```

##Acknowledgement
Originally the SocksPort part of this project was a leaned down, modified and .NET Core ported version of the [SocketToMe](https://github.com/joelverhagen/SocketToMe) project.  
Originally the ControlPort part of this project was a leaned down, modified and .NET Core ported version of the [Tor.NET](https://github.com/sharpbrowser/Tor.NET) project.  
At this point of the development you can still find parts of the former project in the codebase, however the latter has been completely replaced.  
