using DotNetEssentials;
using DotNetTor.Bases;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Fields
{
    public class TotPurpose : ByteArraySerializableBase
    {
		#region Statics

		public static TotPurpose Empty => new TotPurpose(new byte[] { });

		public static TotPurpose Success => new TotPurpose(new byte[] { 0 });

		/// <summary>
		/// The request was malformed.
		/// </summary>
		public static TotPurpose BadRequest => new TotPurpose(new byte[] { 1 });

		public static TotPurpose VersionMismatch => new TotPurpose(new byte[] { 2 });

		/// <summary>
		/// The server was not able to execute the Request properly.
		/// </summary>
		public static TotPurpose UnsuccessfulRequest => new TotPurpose(new byte[] { 3 });

		/// <summary>
		/// The Purpose field of Ping MUST be ping.
		/// </summary>
		public static TotPurpose Ping => new TotPurpose("ping");

		/// <summary>
		/// The Purpose field of Pong MUST be pong.
		/// </summary>
		public static TotPurpose Pong => new TotPurpose("pong");

		#endregion

		#region PropertiesAndMembers

		public byte[] Purpose { get; private set; }

		public int Length => Purpose.Length;

		#endregion

		#region Constructors

		public TotPurpose()
		{

		}
		
		public TotPurpose(byte[] purpose)
		{
			purpose = purpose ?? new byte[] { };
			Guard.MaximumAndNotNull($"{nameof(purpose)}.{nameof(purpose.Length)}", purpose.Length, 255);

			Purpose = purpose;
		}

		/// <summary>
		/// ToT uses UTF8 byte encoding, except for its Content field. 
		/// Encoding of the Content field is arbitrary, the server and the client must have mutual understanding. 
		/// When this document specifies the content as string, it means UTF8 encoding.
		/// </summary>
		public TotPurpose(string purpose) : this(Encoding.UTF8.GetBytes(purpose ?? ""))
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes) => FromBytes(bytes, startsWithLength: false);

		public void FromBytes(byte[] bytes, bool startsWithLength)
		{
			var purposeBytes = bytes;
			if (startsWithLength)
			{
				if (bytes.Length < 1)
				{
					throw new FormatException($"{nameof(purposeBytes)}.{nameof(purposeBytes.Length)} cannot be less than 1. Actual: {purposeBytes.Length}.");
				}

				purposeBytes = bytes.Skip(1).ToArray();
				if(bytes[0] != purposeBytes.Length)
				{
					throw new FormatException($"{nameof(purposeBytes)}.{nameof(purposeBytes.Length)} doesn't equal to the first byte's value. First byte: {(int)bytes[0]}. {nameof(purposeBytes)}.{nameof(purposeBytes.Length)}: {purposeBytes.Length}.");
				}
			}

			if (bytes == null || bytes.Length == 0)
			{
				Purpose = new byte[] { };
				return;
			}

			Purpose = purposeBytes;
		}

		public override byte[] ToBytes() => ToBytes(startsWithLength: false);

		public byte[] ToBytes(bool startsWithLength)
		{
			if(!startsWithLength)
			{
				return Purpose;
			}
			return ByteHelpers.Combine(new byte[] { BitConverter.GetBytes(Length)[0] }, Purpose);
		}

		public override string ToString()
		{
			if (this == Success) return ToHex(xhhSyntax: true) + " " + nameof(Success);
			if (this == BadRequest) return ToHex(xhhSyntax: true) + " " + nameof(BadRequest);
			if (this == VersionMismatch) return ToHex(xhhSyntax: true) + " " + nameof(VersionMismatch);
			if (this == UnsuccessfulRequest) return ToHex(xhhSyntax: true) + " " + nameof(UnsuccessfulRequest);
			return Encoding.UTF8.GetString(Purpose);
		}

		#endregion
	}
}
