using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages.Bases;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetTor.TorOverTcp.Models.Messages
{
	/// <summary>
	/// A Pong MUST follow it.
	/// </summary>
	public class TotPing : TotMessageBase
	{
		#region Statics

		public static TotPing Instance => new TotPing(TotContent.Empty);

		#endregion

		#region ConstructorsAndInitializers

		public TotPing() : base()
		{

		}

		public TotPing(TotContent content) : base(TotMessageType.Ping, TotPurpose.Ping, content)
		{

		}

		#endregion
	}
}
