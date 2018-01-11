using DotNetEssentials;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages.Bases;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Messages
{
	/// <summary>
	/// Issued by the server. It MUST NOT be issued before a SubscribeRequest.
	/// </summary>
	public class TotNotification : TotMessageBase
	{
		#region ConstructorsAndInitializers

		public TotNotification() : base()
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotNotification(string purpose) : this(purpose, TotContent.Empty)
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotNotification(string purpose, TotContent content) : base(TotMessageType.Notification, new TotPurpose(purpose), content)
		{

		}

		#endregion

		#region Serialization

		public override void FromBytes(byte[] bytes)
		{
			Guard.NotNullOrEmpty(nameof(bytes), bytes);

			base.FromBytes(bytes);

			var expectedMessageType = TotMessageType.Notification;
			if (MessageType != expectedMessageType)
			{
				throw new FormatException($"Wrong {nameof(MessageType)}. Expected: {expectedMessageType}. Actual: {MessageType}.");
			}
		}

		#endregion
	}
}
