using DotNetEssentials;
using DotNetTor.Bases;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Fields
{
	public class TotMessageType : OctetSerializableBase
	{
		#region Statics

		/// <summary>
		/// Issued by the client. A Response MUST follow it.
		/// </summary>
		public static TotMessageType Request => new TotMessageType(1);

		/// <summary>
		/// Issued by the server. A Request MUST precede it.
		/// </summary>
		public static TotMessageType Response => new TotMessageType(2);

		/// <summary>
		/// Issued by the client. A Response MUST follow it.
		/// </summary>
		public static TotMessageType SubscribeRequest => new TotMessageType(3);

		/// <summary>
		/// Issued by the server. It MUST NOT be issued before a SubscribeRequest.
		/// </summary>
		public static TotMessageType Notification => new TotMessageType(5);

		/// <summary>
		/// A Pong MUST follow it.
		/// </summary>
		public static TotMessageType Ping => new TotMessageType(6);

		/// <summary>
		/// A Ping MUST precede it.
		/// </summary>
		public static TotMessageType Pong => new TotMessageType(7);

		#endregion

		#region ConstructorsAndInitializers

		public TotMessageType()
		{

		}

		private TotMessageType(int value)
		{
			ByteValue = (byte)Guard.InRangeAndNotNull(nameof(value), value, 0, 255);
		}

		#endregion

		#region Serialization

		public override string ToString()
		{
			if (this == Request) return ToHex(xhhSyntax: true) + " " + nameof(Request);
			if (this == Response) return ToHex(xhhSyntax: true) + " " + nameof(Response);
			if (this == SubscribeRequest) return ToHex(xhhSyntax: true) + " " + nameof(SubscribeRequest);
			if (this == Notification) return ToHex(xhhSyntax: true) + " " + nameof(Notification);
			if (this == Ping) return ToHex(xhhSyntax: true) + " " + nameof(Ping);
			if (this == Pong) return ToHex(xhhSyntax: true) + " " + nameof(Pong);
			return ToHex(xhhSyntax: true);
		}

		#endregion
	}
}
