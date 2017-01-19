using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using DotNetTor.SocksPort.Net;

namespace DotNetTor
{
	internal static class Util
	{
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

		internal const byte SocksVersion = 0x05;

		internal enum AddressType : byte
		{
			IpV4 = 0x01,
			DomainName = 0x03,
			IpV6 = 0x04
		}

		internal enum ReplyType : byte
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

		internal static ArraySegment<byte> BuildConnectToDomainRequest(string domainName, RequestType requestType)
		{
			ArraySegment<byte> sendBuffer;
			int port = 0;
			if (requestType == RequestType.HTTP)
				port = 80;
			else if (requestType == RequestType.HTTPS)
				port = 443;
			byte[] nameBytes = Encoding.ASCII.GetBytes(domainName);
			var addresByteList = new List<byte>();
			foreach (byte b in
				Enumerable.Empty<byte>()
					.Concat(
						new[] { (byte)nameBytes.Length })
					.Concat(nameBytes))
			{
				addresByteList.Add(b);
			}
			byte[] addressBytes = addresByteList.ToArray();

			var sendByteList = new List<byte>();
			foreach (
				byte b in
				Enumerable.Empty<byte>()
					.Concat(new[] {SocksVersion, (byte)0x01, (byte)0x00, (byte)AddressType.DomainName })
					.Concat(addressBytes)
					.Concat(BitConverter.GetBytes(
						IPAddress.HostToNetworkOrder((short)port))))
			{
				sendByteList.Add(b);
			}
			sendBuffer = new ArraySegment<byte>(sendByteList.ToArray());
			return sendBuffer;
		}

		internal static void ValidateConnectToDestinationResponse(ArraySegment<byte> receiveBuffer, int receiveCount)
		{
			if (receiveCount < 7)
			{
				throw new TorException($"The SOCKS5 proxy responded with {receiveCount} bytes to the connect request. At least 7 bytes are expected.");
			}
			byte version = receiveBuffer.Array[0];
			ValidateSocksVersion(version);
			if (receiveBuffer.Array[1] != (byte)ReplyType.Succeeded)
			{
				throw new TorException($"The SOCKS5 proxy responded with a unsuccessful reply type '{(receiveBuffer.Array[1] >= (byte)ReplyType.Unassigned ? ReplyType.Unassigned : (ReplyType)receiveBuffer.Array[1])}' (0x{receiveBuffer.Array[1]:x2}).");
			}
			if (receiveBuffer.Array[2] != 0x00)
			{
				throw new TorException($"The SOCKS5 proxy responded with an unexpected reserved field value 0x{receiveBuffer.Array[2]:x2}. 0x00 was expected.");
			}
			if (!Enum.GetValues(typeof(AddressType)).Cast<byte>().Contains(receiveBuffer.Array[3]))
			{
				throw new TorException($"The SOCKS5 proxy responded with an unexpected {nameof(AddressType)} 0x{receiveBuffer.Array[3]:x2}.");
			}
			var bindAddressType = (AddressType)receiveBuffer.Array[3];
			if (bindAddressType == AddressType.IpV4)
			{
				if (receiveCount != 10)
				{
					throw new TorException($"The SOCKS5 proxy responded with an unexpected number of bytes ({receiveCount} bytes) when the address is an IPv4 address. 10 bytes were expected.");
				}
				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer.Array, 8));
			}
			else if (bindAddressType == AddressType.DomainName)
			{
				byte bindAddressLength = receiveBuffer.Array[4];
				Encoding.ASCII.GetString(receiveBuffer.Array, 5, bindAddressLength);
				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer.Array, 5 + bindAddressLength));
			}
			else if (bindAddressType == AddressType.IpV6)
			{
				if (receiveCount != 22)
				{
					throw new TorException($"The SOCKS5 proxy responded with an unexpected number of bytes ({receiveCount} bytes) when the address is an IPv6 address. 22 bytes were expected.");
				}
				IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer.Array, 20));
			}
			else
			{
				var addressTypeNotSupportedMessage = $"The provided address type '{bindAddressType}' is not supported.";
				throw new NotSupportedException(addressTypeNotSupportedMessage);
			}
		}

		internal static void ValidateHandshakeResponse(ArraySegment<byte> receiveBuffer, int receiveCount)
		{
			if (receiveCount != 2)
			{
				throw new TorException($"The SOCKS5 proxy responded with {receiveCount} bytes, instead of 2, during the handshake.");
			}
			byte version = receiveBuffer.Array[0];
			ValidateSocksVersion(version);
			if (receiveBuffer.Array[1] == 0xFF)
			{
				throw new TorException("The SOCKS5 proxy does not support any of the client's authentication methods.");
			}
		}

		internal static void ValidateSocksVersion(byte version)
		{
			if (version != SocksVersion)
			{
				throw new TorException($"The SOCKS5 proxy responded with 0x{version:x2}, instead of 0x{SocksVersion:x2}, for the SOCKS version number.");
			}
		}

		public static RequestType? GetReqType(Uri uri)
		{
			RequestType? reqType = null;
			if (uri.Port == 80)
				reqType = RequestType.HTTP;
			else if (uri.Port == 443)
				reqType = RequestType.HTTPS;
			if (reqType == null)
				throw new ArgumentException($"{nameof(uri.Port)} cannot be {uri.Port}");
			return reqType;
		}
	}
}