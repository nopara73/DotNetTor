using DotNetTor.SocksPort.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;

namespace DotNetTor.SocksPort
{
	public sealed class Client : IDisposable
	{
		private const byte SocksVersion = 0x05;

		private readonly IPEndPoint _socksEndPoint;

		private Socket _socket;

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

		public Client(string address = "127.0.0.1", int socksPort = 9050)
		{
			try
			{
				_socksEndPoint = new IPEndPoint(IPAddress.Parse(address), socksPort);
			}
			catch (Exception ex)
			{
				throw new TorException("SocksPort client initialization failed.", ex);
			}
		}

		[Obsolete(Shared.SyncMethodDeprecated + ": ConnectAsync()")]
		public NetworkHandler GetHandlerFromDomain(string domainName, RequestType requestType = RequestType.HTTP)
			=> ConnectAsync(domainName, requestType).Result; // Task.Result is fine, because the method is obsolated

		public async Task<NetworkHandler> ConnectAsync(string domainName, RequestType requestType)
		{
			await Util.AssertPortOpenAsync(_socksEndPoint).ConfigureAwait(false);
			try
			{
				_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				await _socket.ConnectAsync(_socksEndPoint).ConfigureAwait(false);

				// HANDSHAKE
				var sendBuffer = new ArraySegment<byte>(new byte[] { 5, 1, 0 });
				await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
				var receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
				var receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
				ValidateHandshakeResponse(receiveBuffer, receiveCount);

				// CONNECT TO DOMAIN DESTINATION
				sendBuffer = BuildConnectToDomainRequest(domainName, requestType);
				await _socket.SendAsync(sendBuffer, SocketFlags.None).ConfigureAwait(false);
				receiveBuffer = new ArraySegment<byte>(new byte[_socket.ReceiveBufferSize]);
				receiveCount = await _socket.ReceiveAsync(receiveBuffer, SocketFlags.None).ConfigureAwait(false);
				ValidateConnectToDestinationResponse(receiveBuffer, receiveCount);

				return new NetworkHandler(_socket);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		private static ArraySegment<byte> BuildConnectToDomainRequest(string domainName, RequestType requestType)
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
					.Concat(new[] { SocksVersion, (byte)0x01, (byte)0x00, (byte)AddressType.DomainName })
					.Concat(addressBytes)
					.Concat(BitConverter.GetBytes(
						IPAddress.HostToNetworkOrder((short)port))))
			{
				sendByteList.Add(b);
			}
			sendBuffer = new ArraySegment<byte>(sendByteList.ToArray());
			return sendBuffer;
		}

		#region Validations
		private static void ValidateConnectToDestinationResponse(ArraySegment<byte> receiveBuffer, int receiveCount)
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

		private static void ValidateHandshakeResponse(ArraySegment<byte> receiveBuffer, int receiveCount)
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

		private static void ValidateSocksVersion(byte version)
		{
			if (version != SocksVersion)
			{
				throw new TorException($"The SOCKS5 proxy responded with 0x{version:x2}, instead of 0x{SocksVersion:x2}, for the SOCKS version number.");
			}
		}
		#endregion
		[Obsolete(Shared.SyncMethodDeprecated + ":ConnectAsync()")]
		public NetworkHandler GetHandlerFromRequestUri(string requestUri)
			=> ConnectAsync(requestUri).Result; // .Result is fine, because the method is obsolated

		public async Task<NetworkHandler> ConnectAsync(string requestUri)
		{
			try
			{
				var uri = new Uri(requestUri);

				RequestType? reqType = null;
				if (uri.Port == 80)
					reqType = RequestType.HTTP;
				else if (uri.Port == 443)
					reqType = RequestType.HTTPS;

				if (reqType == null)
					throw new ArgumentException($"{nameof(uri.Port)} cannot be {uri.Port}");

				return await ConnectAsync(uri.DnsSafeHost, (RequestType)reqType).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				throw new TorException(ex.Message, ex);
			}
		}

		private void ReleaseUnmanagedResources()
		{
			try
			{
				if (_socket.Connected)
					_socket.Shutdown(SocketShutdown.Both);
				_socket.Dispose();
			}
			catch (ObjectDisposedException)
			{
				return;
			}
		}

		public void Dispose()
		{
			ReleaseUnmanagedResources();
			GC.SuppressFinalize(this);
		}

		~Client()
		{
			ReleaseUnmanagedResources();
		}
	}
}