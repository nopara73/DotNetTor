using DotNetEssentials;
using DotNetTor.Bases;
using DotNetTor.TorOverTcp.Models.Fields;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Messages.Bases
{
    public abstract class TotMessageBase : ByteArraySerializableBase
	{
		#region PropertiesAndMembers

		public TotVersion Version { get; set; }

		public TotMessageType MessageType { get; set; }

		public TotPurpose Purpose { get; set; }

		public TotContent Content { get; set; }

		#endregion

		#region ConstructorsAndInitializers

		protected TotMessageBase()
		{

		}

		protected TotMessageBase(TotMessageType messageType, TotPurpose purpose, TotContent content)
		{
			Version = TotVersion.Version1;
			MessageType = Guard.NotNull(nameof(messageType), messageType);
			Purpose = purpose ?? TotPurpose.Empty;
			Content = content ?? TotContent.Empty;
		}

		#endregion

		#region Methods

		/// <summary>
		/// Tor sends data in chunks of 512 bytes, called cells,to make it harder for intermediaries to guess exactly how many bytes are being communicated at each step. 
		/// A developer that intends to build an application on top of ToT MAY utilize this information to gain more efficient network usage.
		/// </summary>
		public int GetNumberOfCells() => ToBytes().Length / 512 + 1;

		/// <summary>
		/// Tor sends data in chunks of 512 bytes, called cells,to make it harder for intermediaries to guess exactly how many bytes are being communicated at each step. 
		/// A developer that intends to build an application on top of ToT MAY utilize this information to gain more efficient network usage.
		/// </summary>
		public int GetLastCellFullnessPercentage()
		{
			int v = GetNumberOfDummyBytesInLastCell();
			decimal v1 = v / 512m;
			return (int)(v1 * 100);
		}

		/// <summary>
		/// Tor sends data in chunks of 512 bytes, called cells,to make it harder for intermediaries to guess exactly how many bytes are being communicated at each step. 
		/// A developer that intends to build an application on top of ToT MAY utilize this information to gain more efficient network usage.
		/// </summary>
		public int GetNumberOfDummyBytesInLastCell() => 512 - (ToBytes().Length % 512);

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);
			Guard.InRangeAndNotNull($"{nameof(bytes)}.{nameof(bytes.Length)}", bytes.Length, 7, 536870912 + 3 + 4 + 255);

			Version = new TotVersion();
			Version.FromByte(bytes[0]);

			MessageType = new TotMessageType();
			MessageType.FromByte(bytes[1]);

			int purposeLength = bytes[2];
			Purpose = new TotPurpose();
			Purpose.FromBytes(bytes.Skip(2).Take(purposeLength + 1).ToArray(), startsWithLength: true);

			int contentLength = BitConverter.ToInt32(bytes.Skip(3 + purposeLength).Take(4).ToArray(), 0);
			Content = new TotContent();
			Content.FromBytes(bytes.Skip(3 + purposeLength).ToArray(), startsWithLength: true);
		}

		public override byte[] ToBytes() => ByteHelpers.Combine(new byte[] { Version.ToByte(), MessageType.ToByte() }, Purpose.ToBytes(startsWithLength: true), Content.ToBytes(startsWithLength: true));

		public override string ToString()
		{
			return $"{Version} {MessageType} {Purpose.Length} {Purpose} {Content.Length} {Content}";
		}

		#endregion
	}
}
