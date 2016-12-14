# DotNetTor
Minimal TOR implementation in .NET Core

##Acknowledgement
The SocksPort stuff in this project modified a part of the [SocketToMe](https://github.com/joelverhagen/SocketToMe) project.  
The ControlPort stuff in this project modified a part of the [Tor.NET](https://github.com/sharpbrowser/Tor.NET) project.

##Nuget
1. Search for DotNetTor.
2. Configure TOR properly

##Build & Test

1. `git clone https://github.com/nopara73/DotNetTor`
2. `cd DotNetTor/`
3. `dotnet restore`
4. `cd src/DotNetTor.Tests/`
5. Configure TOR properly
6. `dotnet test`

##Configure TOR
1. Download TOR Expert Bundle: https://www.torproject.org/download/download
2. Download the torrc config file sample: https://svn.torproject.org/svn/tor/tags/tor-0_0_9_5/src/config/torrc.sample.in
3. Place torrc in the proper default location (depending on your OS) and edit it:
  - Optionally uncomment and edit the SocksPort, if you don't uncomment it will default to 9050 port anyway
  - Uncomment the default ControlPort 9051, optionally edit it
  - Uncomment and modify the password hash
    - To run my tests, for the ControlPortPassword = "ILoveBitcoin21" the hash should be: HashedControlPassword 16:0978DBAF70EEB5C46063F3F6FD8CBC7A86DF70D2206916C1E2AE29EAF6
    - You should use different one, a more complicated one. Figure it out, you can start tor like this: `tor --hash-password password`
      where password is the password you've chosen. It will give you the hashed password.
4. Start tor, it will listen to the ports you set in the config file.

##Usage
```
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
