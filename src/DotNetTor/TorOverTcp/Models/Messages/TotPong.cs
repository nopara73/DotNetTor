using DotNetEssentials;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages.Bases;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Messages
{
	/// <summary>
	/// A Ping MUST precede it.
	/// </summary>
	public class TotPong : TotMessageBase
    {
		#region Statics

		public static TotPong Instance => new TotPong(TotContent.Empty);

		#endregion
		
		#region ConstructorsAndInitializers

		public TotPong() : base()
		{

		}

		public TotPong(TotContent content) : base(TotMessageType.Pong, TotPurpose.Pong, content)
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			base.FromBytes(bytes);

			var expectedMessageType = TotMessageType.Pong;
			if (MessageType != expectedMessageType)
			{
				throw new FormatException($"Wrong {nameof(MessageType)}. Expected: {expectedMessageType}. Actual: {MessageType}.");
			}

			var expectedPurpose = TotPurpose.Pong;
			if (Purpose != expectedPurpose)
			{
				throw new FormatException($"Wrong {nameof(Purpose)}. Expected: {expectedPurpose}. Actual: {Purpose}.");
			}
		}

		#endregion
	}
}
