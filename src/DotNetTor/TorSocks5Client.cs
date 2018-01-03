using Nito.AsyncEx;
using DotNetTor.Exceptions;
using DotNetTor.TorSocks5.TorSocks5.Models.Fields.ByteArrayFields;
using DotNetTor.TorSocks5.Models.Fields.OctetFields;
using DotNetTor.TorSocks5.Models.Messages;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DotNetTor.TorSocks5.Models.TorSocks5.Fields.ByteArrayFields;
using System.IO;

namespace DotNetTor
{
	/// <summary>
	/// Create an instance with the TorSocks5Manager
	/// </summary>
	public class TorSocks5Client : IDisposable
	{
		#region PropertiesAndMembers

		public TcpClient TcpClient { get; private set; }

		public IPEndPoint TorSocks5EndPoint { get; private set; }

		public Stream Stream { get; internal set; }
		
		public string DestinationHost { get; private set; }

		public int DestinationPort { get; private set; }

		public bool IsConnected
		{
			get
			{
				try
				{
					if (TcpClient == null || TcpClient.GetStream() == null)
					{
						return false;
					}

					return TcpClient.Connected;
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
					return false;
				}
			}
		}

		private AsyncLock AsyncLock { get; }

		#endregion

		#region ConstructorsAndInitializers

		internal TorSocks5Client(IPEndPoint endPoint)
		{
			TorSocks5EndPoint = Guard.NotNull(nameof(endPoint), endPoint);
			TcpClient = new TcpClient();
			AsyncLock = new AsyncLock();
		}

		internal async Task ConnectAsync()
		{
			using (await AsyncLock.LockAsync())
			{
				await TcpClient.ConnectAsync(TorSocks5EndPoint.Address, TorSocks5EndPoint.Port).ConfigureAwait(false);
				Stream = TcpClient.GetStream();
			}
		}

		/// <summary>
		/// IsolateSOCKSAuth must be on (on by default)
		/// https://www.torproject.org/docs/tor-manual.html.en
		/// https://gitweb.torproject.org/torspec.git/tree/socks-extensions.txt#n35
		/// </summary>
		internal async Task HandshakeAsync(bool isolateStream = true)
		{
			MethodsField methods;
			if (!isolateStream)
			{
				methods = new MethodsField(MethodField.NoAuthenticationRequired);
			}
			else
			{
				methods = new MethodsField(MethodField.UsernamePassword);
			}

			var sendBuffer = new VersionMethodRequest(methods).ToBytes();

			var receiveBuffer = await SendAsync(sendBuffer, 2).ConfigureAwait(false);

			var methodSelection = new MethodSelectionResponse();
			methodSelection.FromBytes(receiveBuffer);
			if (methodSelection.Ver != VerField.Socks5)
			{
				throw new NotSupportedException($"`SOCKS{methodSelection.Ver.Value}` is not supported. Only SOCKS5 is supported.");
			}
			if (methodSelection.Method == MethodField.NoAcceptableMethods)
			{
				// https://www.ietf.org/rfc/rfc1928.txt
				// If the selected METHOD is X'FF', none of the methods listed by the
				// client are acceptable, and the client MUST close the connection.
				DisposeTcpClient();
				throw new NotSupportedException("Tor's SOCKS5 proxy does not support any of the client's authentication methods.");
			}
			if (methodSelection.Method == MethodField.UsernamePassword)
			{
				// https://tools.ietf.org/html/rfc1929#section-2
				// Once the SOCKS V5 server has started, and the client has selected the
				// Username / Password Authentication protocol, the Username / Password
				// subnegotiation begins.  This begins with the client producing a
				// Username / Password request:
				var username = RandomString.Generate(21);
				var password = RandomString.Generate(21);
				var uName = new UNameField(username);
				var passwd = new PasswdField(password);
				var usernamePasswordRequest = new UsernamePasswordRequest(uName, passwd);
				sendBuffer = usernamePasswordRequest.ToBytes();

				Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
				receiveBuffer = await SendAsync(sendBuffer, 2).ConfigureAwait(false);

				var userNamePasswordResponse = new UsernamePasswordResponse();
				userNamePasswordResponse.FromBytes(receiveBuffer);
				if (userNamePasswordResponse.Ver != usernamePasswordRequest.Ver)
				{
					throw new NotSupportedException($"Authentication version {userNamePasswordResponse.Ver.Value}` is not supported. Only version {usernamePasswordRequest.Ver} is supported.");
				}

				if (!userNamePasswordResponse.Status.IsSuccess()) // In Tor authentication is different, this will never happen;
				{
					// https://tools.ietf.org/html/rfc1929#section-2
					// A STATUS field of X'00' indicates success. If the server returns a
					// `failure' (STATUS value other than X'00') status, it MUST close the
					// connection.
					DisposeTcpClient();
					throw new InvalidOperationException("Wrong username and/or password.");
				}
			}
		}

		internal async Task ConnectToDestinationAsync(IPEndPoint destination)
		{
			Guard.NotNull(nameof(destination), destination);
			await ConnectToDestinationAsync(destination.Address.ToString(), destination.Port).ConfigureAwait(false);
		}

		/// <param name="host">IPv4 or domain</param>
		internal async Task ConnectToDestinationAsync(string host, int port)
		{
			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);
			Guard.MinimumAndNotNull(nameof(port), port, 0);

			var cmd = CmdField.Connect;

			var dstAddr = new AddrField(host);
			DestinationHost = dstAddr.DomainOrIPv4;

			var dstPort = new PortField(port);
			DestinationPort = dstPort.DstPort;

			var connectionRequest = new TorSocks5Request(cmd, dstAddr, dstPort);
			var sendBuffer = connectionRequest.ToBytes();

			var receiveBuffer = await SendAsync(sendBuffer).ConfigureAwait(false);

			var connectionResponse = new TorSocks5Response();
			connectionResponse.FromBytes(receiveBuffer);

			if (connectionResponse.Rep != RepField.Succeeded)
			{
				// https://www.ietf.org/rfc/rfc1928.txt
				// When a reply(REP value other than X'00') indicates a failure, the
				// SOCKS server MUST terminate the TCP connection shortly after sending
				// the reply.This must be no more than 10 seconds after detecting the
				// condition that caused a failure.
				DisposeTcpClient();
				throw new TorSocks5FailureResponseException(connectionResponse.Rep);
			}

			// Don't check the Bnd. Address and Bnd. Port. because Tor doesn't seem to return any, ever. It returns zeros instead.
			// Generally also don't check anything but the success response, according to Socks5 RFC

			// If the reply code(REP value of X'00') indicates a success, and the
			// request was either a BIND or a CONNECT, the client may now start
			// passing data.  If the selected authentication method supports
			// encapsulation for the purposes of integrity, authentication and / or
			// confidentiality, the data are encapsulated using the method-dependent
			// encapsulation.Similarly, when data arrives at the SOCKS server for
			// the client, the server MUST encapsulate the data as appropriate for 
			// the authentication method in use.
		}
		
		public void AssertConnected()
		{
			if (!IsConnected)
			{
				throw new ConnectionException($"`{nameof(TorSocks5Client)}` is not connected to `{TorSocks5EndPoint}`.");
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Sends bytes to the Tor Socks5 connection
		/// </summary>
		/// <param name="sendBuffer">Sent data</param>
		/// <param name="receiveBufferSize">Maximum number of bytes expected to be received in the reply</param>
		/// <returns>Reply</returns>
		public async Task<byte[]> SendAsync(byte[] sendBuffer, int? receiveBufferSize = null)
		{
			Guard.NotNullOrEmpty(nameof(sendBuffer), sendBuffer);

			using (await AsyncLock.LockAsync())
			{
				AssertConnected();
				var stream = TcpClient.GetStream();

				await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length).ConfigureAwait(false);
				await stream.FlushAsync().ConfigureAwait(false);

				int recBuffSize = receiveBufferSize ?? TcpClient.ReceiveBufferSize;
				var receiveBuffer = new byte[recBuffSize];
				var receiveCount = await stream.ReadAsync(receiveBuffer, 0, receiveBuffer.Length).ConfigureAwait(false);
				if (receiveCount <= 0)
				{
					throw new ConnectionException($"Not connected to Tor SOCKS5 proxy: `{TorSocks5EndPoint}`.");
				}
				return receiveBuffer.Take(receiveCount).ToArray();
			}
		}

		/// <summary>
		/// When Tor receives a "RESOLVE" SOCKS command, it initiates
		/// a remote lookup of the hostname provided as the target address in the SOCKS
		/// request.
		/// </summary>
		internal async Task<IPAddress> ResolveAsync(string host)
		{
			// https://gitweb.torproject.org/torspec.git/tree/socks-extensions.txt#n44

			host = Guard.NotNullOrEmptyOrWhitespace(nameof(host), host, true);

			var cmd = CmdField.Resolve;

			var dstAddr = new AddrField(host);

			var dstPort = new PortField(0);

			var resolveRequest = new TorSocks5Request(cmd, dstAddr, dstPort);
			var sendBuffer = resolveRequest.ToBytes();

			var receiveBuffer = await SendAsync(sendBuffer).ConfigureAwait(false);

			var resolveResponse = new TorSocks5Response();
			resolveResponse.FromBytes(receiveBuffer);

			if (resolveResponse.Rep != RepField.Succeeded)
			{
				throw new TorSocks5FailureResponseException(resolveResponse.Rep);
			}
			return IPAddress.Parse(resolveResponse.BndAddr.DomainOrIPv4);
		}

		/// <summary>
		/// Tor attempts to find the canonical hostname for that IPv4 record
		/// </summary>
		internal async Task<string> ReverseResolveAsync(IPAddress iPv4)
		{
			// https://gitweb.torproject.org/torspec.git/tree/socks-extensions.txt#n55

			Guard.NotNull(nameof(iPv4), iPv4);
			Guard.Same($"{nameof(iPv4)}.{nameof(iPv4.AddressFamily)}", AddressFamily.InterNetwork, iPv4.AddressFamily);

			var cmd = CmdField.ResolvePtr;

			var dstAddr = new AddrField(iPv4.ToString());

			var dstPort = new PortField(0);

			var resolveRequest = new TorSocks5Request(cmd, dstAddr, dstPort);
			var sendBuffer = resolveRequest.ToBytes();

			var receiveBuffer = await SendAsync(sendBuffer).ConfigureAwait(false);

			var resolveResponse = new TorSocks5Response();
			resolveResponse.FromBytes(receiveBuffer);

			if (resolveResponse.Rep != RepField.Succeeded)
			{
				throw new TorSocks5FailureResponseException(resolveResponse.Rep);
			}
			return resolveResponse.BndAddr.DomainOrIPv4;
		}

		#endregion

		#region IDisposable Support

		private volatile bool _disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					DisposeTcpClient();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				_disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~TorSocks5Client() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}

		private void DisposeTcpClient()
		{
			try
			{
				if(TcpClient != null)
				{
					if (TcpClient.Connected)
					{
						Stream?.Dispose();
					}
					TcpClient?.Dispose();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			finally
			{
				TcpClient = null; // need to be called, .net bug
			}
		}

		#endregion
	}
}
