using DotNetEssentials;
using DotNetEssentials.Logging;
using DotNetTor.Http.Models;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
	public class TorSocks5Handler : HttpMessageHandler
	{
		#region PropertiesAndMembers

		public ConcurrentDictionary<TorSocks5Client, AsyncLock> Connections { get; }

		public TorSocks5Manager TorSocks5Manager { get; }

		public IPEndPoint TorSocks5EndPoint => TorSocks5Manager?.TorSocks5EndPoint;

		private AsyncLock ConnectLock { get; }
		private AsyncLock LinuxOsxLock { get; }
		private AsyncReaderWriterLock DisposeRequestLock { get; }

		#endregion

		#region ConstructorsAndInitializers

		public TorSocks5Handler(IPEndPoint torSocks5EndPoint)
		{
			Guard.NotNull(nameof(torSocks5EndPoint), torSocks5EndPoint);

			TorSocks5Manager = new TorSocks5Manager(torSocks5EndPoint);

			Connections = new ConcurrentDictionary<TorSocks5Client, AsyncLock>();

			ConnectLock = new AsyncLock();

			LinuxOsxLock = new AsyncLock();

			DisposeRequestLock = new AsyncReaderWriterLock();

			TorControlClient.CircuitChangeRequested += Client_CircuitChangeRequestedAsync;
		}

		#endregion

		#region Methods

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
		{
			Guard.NotNull(nameof(request), request);
			if (cancel == null)
			{
				cancel = CancellationToken.None;
			}
			// https://tools.ietf.org/html/rfc7230#section-2.7.1
			// A sender MUST NOT generate an "http" URI with an empty host identifier.
			var host = Guard.NotNullOrEmptyOrWhitespace($"{nameof(request)}.{nameof(request.RequestUri)}.{nameof(request.RequestUri.DnsSafeHost)}", request.RequestUri.DnsSafeHost, trim: true);

			using (var linuxOsxLock = await LinuxOsxLock.LockAsync(cancel).ConfigureAwait(false)) // Linux and OSX is terrible, rather do everything in sync, if windows, it'll be released right away, this must be in effect to bypass this linuxosxbug: if (client != null && (!client.IsConnected || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))
			using (await DisposeRequestLock.ReaderLockAsync(cancel).ConfigureAwait(false))
			using (var connectLockTask = await ConnectLock.LockAsync(cancel).ConfigureAwait(false)) // this makes sure clients with the same host don't try to connect concurrently, it gets released after connection established
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					linuxOsxLock.Dispose();
				}

				KeyValuePair<TorSocks5Client, AsyncLock> clientLockPair = TryFindClientLockPair(host, request.RequestUri.Port);
				AsyncLock clientLock = clientLockPair.Value ?? new AsyncLock(); // this makes sure clients with the same host don't work concurrently
				using (await clientLock.LockAsync(cancel).ConfigureAwait(false))
				{
					TorSocks5Client client = await SendAsync(request, host, connectLockTask, clientLockPair, clientLock, cancel);

					try
					{
						return await new HttpResponseMessage().CreateNewAsync(client.Stream, request.Method).ConfigureAwait(false);
					}
					catch (IOException)
					{
						// the connection is lost, reconnect
						client = await SendAsync(request, host, connectLockTask, clientLockPair, clientLock, cancel);
						return await new HttpResponseMessage().CreateNewAsync(client.Stream, request.Method).ConfigureAwait(false);
					}
				}
			}
		}

		private async Task<TorSocks5Client> SendAsync(HttpRequestMessage request, string host, IDisposable connectLockTask, KeyValuePair<TorSocks5Client, AsyncLock> clientLockPair, AsyncLock clientLock, CancellationToken cancel)
		{
			TorSocks5Client client = null;
			try
			{
				// https://tools.ietf.org/html/rfc7230#section-2.6
				// Intermediaries that process HTTP messages (i.e., all intermediaries
				// other than those acting as tunnels) MUST send their own HTTP - version
				// in forwarded messages.
				request.Version = HttpProtocol.HTTP11.Version;

				client = clientLockPair.Key;

				if (client != null && (!client.IsConnected || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))) // Linux and OSX bug, this line only works if LinuxOsxLock is in effect
				{
					Connections.TryRemove(client, out AsyncLock al);
					client?.Dispose();
				}

				if (client == null || !client.IsConnected)
				{
					cancel.ThrowIfCancellationRequested();
					client = await TorSocks5Manager.EstablishTcpConnectionAsync(host, request.RequestUri.Port, isolateStream: true, cancel: cancel).ConfigureAwait(false);
					cancel.ThrowIfCancellationRequested();

					Stream stream = client.TcpClient.GetStream();
					if (request.RequestUri.Scheme.Equals("https", StringComparison.Ordinal))
					{
						SslStream sslStream;
						// On Linux and OSX ignore certificate, because of a .NET Core bug
						// This is a security vulnerability, has to be fixed as soon as the bug get fixed
						// Details:
						// https://github.com/dotnet/corefx/issues/21761
						// https://github.com/nopara73/DotNetTor/issues/4
						if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						{
							sslStream = new SslStream(
								stream,
								leaveInnerStreamOpen: true);
						}
						else
						{
							sslStream = new SslStream(
								stream,
								leaveInnerStreamOpen: true,
								userCertificateValidationCallback: (a, b, c, d) => true);
						}

						await sslStream
							.AuthenticateAsClientAsync(
								host,
								new X509CertificateCollection(),
								SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12,
								checkCertificateRevocation: true)
							.ConfigureAwait(false);
						stream = sslStream;
					}

					client.Stream = stream;

					Connections.TryAdd(client, clientLock);
				}
				connectLockTask?.Dispose();

				cancel.ThrowIfCancellationRequested();

				// https://tools.ietf.org/html/rfc7230#section-3.3.2
				// A user agent SHOULD send a Content - Length in a request message when
				// no Transfer-Encoding is sent and the request method defines a meaning
				// for an enclosed payload body.For example, a Content - Length header
				// field is normally sent in a POST request even when the value is 0
				// (indicating an empty payload body).A user agent SHOULD NOT send a
				// Content - Length header field when the request message does not contain
				// a payload body and the method semantics do not anticipate such a
				// body.
				// TODO implement it fully (altough probably .NET already ensures it)
				if (request.Method == HttpMethod.Post)
				{
					if (request.Headers.TransferEncoding.Count == 0)
					{
						if (request.Content == null)
						{
							request.Content = new ByteArrayContent(new byte[] { }); // dummy empty content
							request.Content.Headers.ContentLength = 0;
						}
						else
						{
							if (request.Content.Headers.ContentLength == null)
							{
								request.Content.Headers.ContentLength = (await request.Content.ReadAsStringAsync().ConfigureAwait(false)).Length;
								cancel.ThrowIfCancellationRequested();
							}
						}
					}
				}

				var requestString = await request.ToHttpStringAsync().ConfigureAwait(false);
				cancel.ThrowIfCancellationRequested();

				var bytes = Encoding.UTF8.GetBytes(requestString);

				try
				{
					await client.Stream.WriteAsync(bytes, 0, bytes.Length, cancel).ConfigureAwait(false);
					cancel.ThrowIfCancellationRequested();
				}
				catch (NullReferenceException ex) // dotnet brainfart
				{
					Logger.LogTrace<TorSocks5Handler>(ex);
					throw new OperationCanceledException();
				}

				await client.Stream.FlushAsync(cancel).ConfigureAwait(false);
				cancel.ThrowIfCancellationRequested();
				return client;
			}
			catch (OperationCanceledException)
			{
				client?.Dispose();
				throw;
			}
			catch(Exception ex)
			{
				Logger.LogTrace<TorSocks5Handler>(ex);
				client?.Dispose();
				cancel.ThrowIfCancellationRequested();
				throw;
			}
		}

		private KeyValuePair<TorSocks5Client, AsyncLock> TryFindClientLockPair(string host, int port)
		{
			return Connections.Where(
				x => x.Key.DestinationHost.Equals(host, StringComparison.OrdinalIgnoreCase, trimmed: true)
				&& x.Key.DestinationPort == port).FirstOrDefault();
		}

		#endregion

		#region Events

		private void Client_CircuitChangeRequestedAsync(object sender, EventArgs e)
		{
			DisposeConnections();
		}

		private void DisposeConnections()
		{
			using (DisposeRequestLock.WriterLock())
			{
				foreach (var connection in Connections)
				{
					connection.Key?.Dispose();
				}
				Connections.Clear();
			}
		}

		#endregion

		#region IDisposable Support

		private volatile bool _disposedValue = false; // To detect redundant calls

		protected override void Dispose(bool disposing)
		{
			try
			{
				if (!_disposedValue)
				{
					try
					{
						TorControlClient.CircuitChangeRequested -= Client_CircuitChangeRequestedAsync;
						DisposeConnections();
					}
					catch (Exception ex)
					{
						Logger.LogWarning<TorSocks5Handler>(ex, LogLevel.Debug);
					}
					finally
					{
						_disposedValue = true;
					}
				}

				base.Dispose(disposing);
			}
			catch(Exception ex)
			{
				Logger.LogWarning<TorSocks5Handler>(ex, LogLevel.Debug);
			}
		}

		~TorSocks5Handler()
		{
			Dispose(false);
		}

		#endregion
	}
}
