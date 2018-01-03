namespace DotNetTor.Interfaces
{
	public interface IByteArraySerializable
    {
		byte[] ToBytes();
		void FromBytes(params byte[] bytes);
		string ToHex();
		void FromHex(string hex);
	}
}
