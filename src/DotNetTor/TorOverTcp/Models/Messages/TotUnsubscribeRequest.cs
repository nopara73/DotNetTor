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
	public class TotUnsubscribeRequest : TotMessageBase
	{
		#region ConstructorsAndInitializers

		public TotUnsubscribeRequest() : base()
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest, UnsubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotUnsubscribeRequest(string purpose) : this(purpose, TotContent.Empty)
		{

		}

		/// <param name="purpose">The Purpose of SubscribeRequest, UnsubscribeRequest and Notification is arbitrary, but clients and servers MUST implement the same Purpose for all three.</param>
		public TotUnsubscribeRequest(string purpose, TotContent content) : base(TotMessageType.UnsubscribeRequest, new TotPurpose(purpose), content)
		{

		}

		#endregion
	}
}
