using DotNetEssentials;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetTor
{
	public class TorSocks5Manager
    {
		#region PropertiesAndMembers

		public IPEndPoint TorSocks5EndPoint { get; private set; }

		#endregion

		#region ConstructorsAndInitializers

		/// <param name="endPoint">Opt out Tor with null.</param>
		public TorSocks5Manager(IPEndPoint endPoint)
		{
			TorSocks5EndPoint = endPoint;
		}

		#endregion

		#region Methods

		public async Task<TorSocks5Client> EstablishTcpConnectionAsync(IPEndPoint destination, bool isolateStream = true)
		{
			Guard.NotNull(nameof(destination), destination);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(isolateStream);
			await client.ConnectToDestinationAsync(destination);
			return client;
		}
		
		/// <param name="identity">Isolates streams by identity.</param>
		public async Task<TorSocks5Client> EstablishTcpConnectionAsync(IPEndPoint destination, string identity)
		{
			identity = Guard.NotNullOrEmptyOrWhitespace(nameof(identity), identity, trim: true);
			Guard.NotNull(nameof(destination), destination);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(identity);
			await client.ConnectToDestinationAsync(destination);
			return client;
		}

		public async Task<TorSocks5Client> EstablishTcpConnectionAsync(string host, int port, bool isolateStream = true)
		{
			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);
			Guard.MinimumAndNotNull(nameof(port), port, 0);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(isolateStream);
			await client.ConnectToDestinationAsync(host, port);
			return client;
		}

		public async Task<TotClient> EstablishTotConnectionAsync(IPEndPoint destination, bool isolateStream = true)
		{
			Guard.NotNull(nameof(destination), destination);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(isolateStream);
			await client.ConnectToDestinationAsync(destination);
			return new TotClient(client);
		}

		/// <param name="identity">Isolates streams by identity.</param>
		public async Task<TotClient> EstablishTotConnectionAsync(IPEndPoint destination, string identity)
		{
			identity = Guard.NotNullOrEmptyOrWhitespace(nameof(identity), identity, trim: true);
			Guard.NotNull(nameof(destination), destination);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(identity);
			await client.ConnectToDestinationAsync(destination);
			return new TotClient(client);
		}

		public async Task<TotClient> EstablishTotConnectionAsync(string host, int port, bool isolateStream = true)
		{
			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);
			Guard.MinimumAndNotNull(nameof(port), port, 0);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(isolateStream);
			await client.ConnectToDestinationAsync(host, port);
			return new TotClient(client);
		}

		/// <param name="identity">Isolates streams by identity.</param>
		public async Task<TorSocks5Client> EstablishTcpConnectionAsync(string host, int port, string identity)
		{
			identity = Guard.NotNullOrEmptyOrWhitespace(nameof(identity), identity, trim: true);
			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);
			Guard.MinimumAndNotNull(nameof(port), port, 0);

			var client = new TorSocks5Client(TorSocks5EndPoint);
			await client.ConnectAsync();
			await client.HandshakeAsync(identity);
			await client.ConnectToDestinationAsync(host, port);
			return client;
		}

		/// <summary>
		/// When Tor receives a "RESOLVE" SOCKS command, it initiates
		/// a remote lookup of the hostname provided as the target address in the SOCKS
		/// request.
		/// </summary>
		public async Task<IPAddress> ResolveAsync(string host, bool isolateStream = true)
		{
			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);
			
			using (var client = new TorSocks5Client(TorSocks5EndPoint))
			{
				await client.ConnectAsync();
				await client.HandshakeAsync(isolateStream);
				return await client.ResolveAsync(host);
			}
		}

		/// <summary>
		/// Tor attempts to find the canonical hostname for that IPv4 record
		/// </summary>
		public async Task<string> ReverseResolveAsync(IPAddress iPv4, bool isolateStream = true)
		{
			Guard.NotNull(nameof(iPv4), iPv4);
			
			using (var client = new TorSocks5Client(TorSocks5EndPoint))
			{
				await client.ConnectAsync();
				await client.HandshakeAsync(isolateStream);
				return await client.ReverseResolveAsync(iPv4);
			}
		}

		#endregion
	}
}
