using DotNetTor.TorSocks5.Models.Fields.OctetFields;
using System;

namespace DotNetTor.Exceptions
{
	public class TorSocks5FailureResponseException : Exception
	{
		public TorSocks5FailureResponseException(RepField rep) : base($"Tor SOCKS5 proxy responded with {rep.ToString()}.")
		{

		}
	}
}
