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
	public class TotSubscribeRequest : TotMessageBase
	{
		#region ConstructorsAndInitializers

		public TotSubscribeRequest() : base()
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotSubscribeRequest(string purpose) : this(purpose, TotContent.Empty)
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotSubscribeRequest(string purpose, TotContent content) : base(TotMessageType.SubscribeRequest, new TotPurpose(purpose), content)
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			base.FromBytes(bytes);

			var expectedMessageType = TotMessageType.SubscribeRequest;
			if (MessageType != expectedMessageType)
			{
				throw new FormatException($"Wrong {nameof(MessageType)}. Expected: {expectedMessageType}. Actual: {MessageType}.");
			}
		}

		#endregion
	}
}
