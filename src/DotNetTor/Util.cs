using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor
{
	internal static class Util
	{
		public static readonly AsyncLock AsyncLock = new AsyncLock();

		public static bool Contains(this string source, string toCheck, StringComparison comp)
		{
			return source.IndexOf(toCheck, comp) >= 0;
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

		public static string ByteArrayToString(byte[] ba)
		{
			string hex = BitConverter.ToString(ba);
			return hex.Replace("-", "");
		}
	}

	internal static class Retry
	{
		internal static async Task DoAsync(
			Action action,
			TimeSpan retryInterval,
			int retryCount = 3) => await DoAsync<object>(() =>
		{
			action();
			return null;
		}, retryInterval, retryCount).ConfigureAwait(false);

		internal static async Task<T> DoAsync<T>(
			Func<T> action,
			TimeSpan retryInterval,
			int retryCount = 3)
		{
			var exceptions = new List<Exception>();

			for (int retry = 0; retry < retryCount; retry++)
			{
				try
				{
					if (retry > 0)
						await Task.Delay(retryInterval).ConfigureAwait(false);
					return action();
				}
				catch (Exception ex)
				{
					exceptions.Add(ex);
				}
			}

			throw new AggregateException(exceptions);
		}		
	}
}