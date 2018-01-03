namespace DotNetTor.Interfaces
{
	public interface IByteSerializable
    {
		byte ToByte();
		void FromByte(byte b);
		string ToHex();
		void FromHex(string hex);
	}
}
