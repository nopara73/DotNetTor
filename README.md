# DotNetTor
Library implementation of essential Tor features in .NET Core.

## See detailed documentation on [CodeProject](https://www.codeproject.com/script/Articles/ArticleVersion.aspx?waid=225577&aid=1161078)
  
##[Nuget](https://www.nuget.org/packages/DotNetTor)

##Build & Test

1. `git clone https://github.com/nopara73/DotNetTor`
2. `cd DotNetTor/`
3. `dotnet restore`
4. `cd src/DotNetTor.Tests/`
5. Configure Tor properly.
6. `dotnet test`

##Configure Tor
1. Download Tor Expert Bundle: https://www.torproject.org/download/download
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

// 2. Get Tor IP
using (var httpClient = new HttpClient(new SocksPortHandler("127.0.0.1", socksPort: 9050)))
{
	var message = httpClient.GetAsync(requestUri).Result;
	var content = message.Content.ReadAsStringAsync().Result;
	Console.WriteLine($"Your Tor IP: \t\t{content}");

	// 3. Change Tor IP
	var controlPortClient = new ControlPort.Client("127.0.0.1", controlPort: 9051, password: "ILoveBitcoin21");
	controlPortClient.ChangeCircuitAsync().Wait();

	// 4. Get changed Tor IP
	message = httpClient.GetAsync(requestUri).Result;
	content = message.Content.ReadAsStringAsync().Result;
	Console.WriteLine($"Your other Tor IP: \t{content}");
}
```

##Acknowledgement
Originally the SocksPort part of this project was a leaned down, modified and .NET Core ported version of the [SocketToMe](https://github.com/joelverhagen/SocketToMe) project.  
Originally the ControlPort part of this project was a leaned down, modified and .NET Core ported version of the [Tor.NET](https://github.com/sharpbrowser/Tor.NET) project.  
At this point of the development you can still find parts of the former project in the codebase, however the latter has been completely replaced.  
