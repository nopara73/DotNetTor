using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
	internal static class Util
	{
		public static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

		internal static async Task AssertPortOpenAsync(IPEndPoint ipEndPoint)
		{
			using (TcpClient tcpClient = new TcpClient())
			{
				try
				{
					await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					throw new TorException($"{ipEndPoint.Address}:{ipEndPoint.Port} is closed.", ex);
				}
			}
		}

		private const byte SocksVersion = 0x05;

		private enum AddressType : byte
		{
			IpV4 = 0x01,
			DomainName = 0x03,
			IpV6 = 0x04
		}

		private enum ReplyType : byte
		{
			Succeeded = 0x00,
			GeneralSocksServerFailure = 0x01,
			ConnectionNotAllowedByRuleset = 0x02,
			NetworkUnreachable = 0x03,
			HostUnreachable = 0x04,
			ConnectionRefused = 0x05,
			TtlExpired = 0x06,
			CommandNotSupport = 0x07,
			AddressTypeNotSupported = 0x08,
			Unassigned = 0x09
		}

		internal static ArraySegment<byte> BuildConnectToUri(Uri uri)
		{
			ArraySegment<byte> sendBuffer;
			int port = uri.Port;
			byte[] nameBytes = Encoding.ASCII.GetBytes(uri.DnsSafeHost);

			var addressBytes =
				Enumerable.Empty<byte>()
				.Concat(new[] {(byte) nameBytes.Length})
				.Concat(nameBytes).ToArray();

			sendBuffer =
				new ArraySegment<byte>(
					Enumerable.Empty<byte>()
					.Concat(
						new[]
						{
							SocksVersion, (byte) 0x01, (byte) 0x00, (byte) AddressType.DomainName
						})
						.Concat(addressBytes)
						.Concat(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short) port))).ToArray());
			return sendBuffer;
		}

		internal static void ValidateConnectToDestinationResponse(ArraySegment<byte> receiveBuffer, int receiveCount)
			=> ValidateConnectToDestinationResponse(receiveBuffer.Array, receiveCount);

		internal static void ValidateConnectToDestinationResponse(byte[] receiveBuffer, int receiveCount)
		{
			if (receiveCount < 7)
			{
				throw new TorException($"The SOCKS5 proxy responded with {receiveCount} bytes to the connect request. At least 7 bytes are expected.");
			}

			byte version = receiveBuffer[0];
			ValidateSocksVersion(version);
			if (receiveBuffer[1] != (byte)ReplyType.Succeeded)
			{
				throw new TorException($"The SOCKS5 proxy responded with a unsuccessful reply type '{(receiveBuffer[1] >= (byte)ReplyType.Unassigned ? ReplyType.Unassigned : (ReplyType)receiveBuffer[1])}' (0x{receiveBuffer[1]:x2}).");
			}
			if (receiveBuffer[2] != 0x00)
			{
				throw new TorException($"The SOCKS5 proxy responded with an unexpected reserved field value 0x{receiveBuffer[2]:x2}. 0x00 was expected.");
			}
			if (!Enum.GetValues(typeof(AddressType)).Cast<byte>().Contains(receiveBuffer[3]))
			{
				throw new TorException($"The SOCKS5 proxy responded with an unexpected {nameof(AddressType)} 0x{receiveBuffer[3]:x2}.");
			}

			var bindAddressType = (AddressType)receiveBuffer[3];
			if (bindAddressType == AddressType.IpV4)
			{
				if (receiveCount != 10)
				{
					throw new TorException($"The SOCKS5 proxy responded with an unexpected number of bytes ({receiveCount} bytes) when the address is an IPv4 address. 10 bytes were expected.");
				}

				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer, 8));
			}
			else if (bindAddressType == AddressType.DomainName)
			{
				byte bindAddressLength = receiveBuffer[4];
				Encoding.ASCII.GetString(receiveBuffer, 5, bindAddressLength);
				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer, 5 + bindAddressLength));
			}
			else if (bindAddressType == AddressType.IpV6)
			{
				if (receiveCount != 22)
				{
					throw new TorException($"The SOCKS5 proxy responded with an unexpected number of bytes ({receiveCount} bytes) when the address is an IPv6 address. 22 bytes were expected.");
				}

				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer, 20));
			}
			else
			{
				var addressTypeNotSupportedMessage = $"The provided address type '{bindAddressType}' is not supported.";
				throw new NotSupportedException(addressTypeNotSupportedMessage);
			}
		}

		internal static void ValidateHandshakeResponse(ArraySegment<byte> receiveBuffer, int receiveCount) => ValidateHandshakeResponse(receiveBuffer.Array, receiveCount);

		internal static void ValidateHandshakeResponse(byte[] receiveBuffer, int receiveCount)
		{
			if(receiveCount != 2)
			{
				throw new TorException($"The SOCKS5 proxy responded with {receiveCount} bytes, instead of 2, during the handshake.");
			}

			byte version = receiveBuffer[0];
			ValidateSocksVersion(version);
			if (receiveBuffer[1] == 0xFF)
			{
				throw new TorException("The SOCKS5 proxy does not support any of the client's authentication methods.");
			}
		}

		private static void ValidateSocksVersion(byte version)
		{
			if (version != SocksVersion)
			{
				throw new TorException($"The SOCKS5 proxy responded with 0x{version:x2}, instead of 0x{SocksVersion:x2}, for the SOCKS version number.");
			}
		}

		internal const string SyncMethodDeprecated = "For better performance consider using the async API instead.";
		internal const string ClassDeprecated = "This class is deprecated.";

		internal static Uri StripPath(Uri requestUri)
		{
			var builder = new UriBuilder
			{
				Scheme = requestUri.Scheme,
				Port = requestUri.Port,
				Host = requestUri.Host
			};
			return builder.Uri;
		}

		internal static void ValidateRequest(HttpRequestMessage request)
		{
			if (!request.RequestUri.Scheme.Equals("http", StringComparison.Ordinal) &&
				!request.RequestUri.Scheme.Equals("https", StringComparison.Ordinal))
				throw new NotSupportedException("Only HTTP and HTTPS are supported.");

			if (!Equals(request.Version, new Version(1, 1)))
				throw new NotSupportedException("Only HTTP/1.1 is supported.");
		}
	}

	internal static class Retry
	{
		internal static void Do(
			Action action,
			TimeSpan retryInterval,
			int retryCount = 3) => Do<object>(() =>
		{
			action();
			return null;
		}, retryInterval, retryCount);

		internal static T Do<T>(
			Func<T> action,
			TimeSpan retryInterval,
			int retryCount = 3)
		{
			Exception exception = null;

			for (int retry = 0; retry < retryCount; retry++)
			{
				try
				{
					if (retry > 0)
						Task.Delay(retryInterval);
					return action();
				}
				catch (Exception ex)
				{
					exception = ex;
				}
			}

			// ReSharper disable once PossibleNullReferenceException
			throw exception;
		}
	}
}