using DotNetEssentials;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages.Bases;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Messages
{
	/// <summary>
	/// Issued by the client. A Response MUST follow it.
	/// </summary>
	public class TotRequest : TotMessageBase
	{
		#region ConstructorsAndInitializers

		public TotRequest() : base()
		{

		}

		/// <param name="purpose">The Purpose of Request is arbitrary.</param>
		public TotRequest(string purpose) : this(purpose, TotContent.Empty)
		{

		}

		/// <param name="purpose">The Purpose of Request is arbitrary.</param>
		public TotRequest(string purpose, TotContent content) : base(TotMessageType.Request, new TotPurpose(purpose), content)
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			base.FromBytes(bytes);

			var expectedMessageType = TotMessageType.Request;
			if (MessageType != expectedMessageType)
			{
				throw new FormatException($"Wrong {nameof(MessageType)}. Expected: {expectedMessageType}. Actual: {MessageType}.");
			}
		}

		#endregion
	}
}
