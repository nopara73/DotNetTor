using DotNetEssentials;
using DotNetTor.Bases;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Fields
{
    public class TotContent : ByteArraySerializableBase
	{
		#region Statics

		public static TotContent Empty => new TotContent(new byte[] { });

		/// <summary>
		/// For a Request to a SubscribeNotify channel the server MUST respond with BadRequest, where the Content is: Cannot send Request to a SubscribeNotify channel.
		/// </summary>
		public static TotContent CantRequestSubscribeNotifyChannel => new TotContent("Cannot send Request to a SubscribeNotify channel.");

		/// <summary>
		/// For a SubscribeRequest to a RequestResponse channel the server MUST respond with BadRequest, where the Content is: Cannot send SubscribeRequest to a RequestResponse channel.
		/// </summary>
		public static TotContent CantSubscribeRequestRequestResponseChannel => new TotContent("Cannot send SubscribeRequest to a RequestResponse channel.");

		#endregion

		#region PropertiesAndMembers

		public byte[] Content { get; private set; }

		public int Length => Content.Length;

		#endregion

		#region Constructors

		public TotContent()
		{

		}

		/// <summary>
		/// If the Response is other than Success, the Content MAY hold the details of the error.
		/// </summary>
		/// <param name="content">Maximum length in bytes: 536870912</param>
		public TotContent(byte[] content)
		{
			content = content ?? new byte[] { };

			// 536870912 byte is 512MB and the maximum number of bytes the Content field can hold.
			Guard.MaximumAndNotNull($"{nameof(content)}.{nameof(content.Length)}", content.Length, 536870912);

			Content = content;
		}

		/// <summary>
		/// If the Response is other than Success, the Content MAY hold the details of the error.
		/// </summary>
		/// <param name="content">Maximum length in bytes: 536870912</param>
		public TotContent(string content, Encoding encoding) : this(encoding.GetBytes(content ?? ""))
		{

		}

		/// <summary>
		/// If the Response is other than Success, the Content MAY hold the details of the error.
		/// </summary>
		/// <param name="content">Maximum length in bytes: 536870912</param>
		public TotContent(string content) : this(content, Encoding.UTF8)
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes) => FromBytes(bytes, startsWithLength: false);

		public void FromBytes(byte[] bytes, bool startsWithLength)
		{
			var contentBytes = bytes;
			if (startsWithLength)
			{
				if (bytes.Length < 4)
				{
					throw new FormatException($"{nameof(contentBytes)}.{nameof(contentBytes.Length)} cannot be less than 4. Actual: {contentBytes.Length}.");
				}

				contentBytes = bytes.Skip(4).ToArray();
				int startingLength = BitConverter.ToInt32(bytes.Take(4).ToArray(), 0);
				// 536870912 byte is 512MB and the maximum number of bytes the Content field can hold.
				// At deserialization, compliant implementations MUST validate the ContentLength field is within range.
				if (startingLength < 0) throw new FormatException($"{nameof(startingLength)} must be minimum at least 0. Value: {startingLength}.");
				if(startingLength > 536870912) throw new FormatException($"{nameof(startingLength)} must be minimum at maximum 536870912. Value: {startingLength}.");

				if (startingLength != contentBytes.Length)
				{
					throw new FormatException($"{nameof(contentBytes)}.{nameof(contentBytes.Length)} doesn't equal to the signaled {nameof(startingLength)}. {nameof(startingLength)}: {startingLength}. {nameof(contentBytes)}.{nameof(contentBytes.Length)}: {contentBytes.Length}.");
				}
			}

			if (bytes == null || bytes.Length == 0)
			{
				Content = new byte[] { };
				return;
			}

			Content = contentBytes;
		}

		public override byte[] ToBytes() => ToBytes(startsWithLength: false);

		public byte[] ToBytes(bool startsWithLength)
		{
			if (!startsWithLength)
			{
				return Content;
			}

			return ByteHelpers.Combine(BitConverter.GetBytes(Length), Content);
		}

		public override string ToString() => ToString(Encoding.UTF8);

		#endregion
	}
}
