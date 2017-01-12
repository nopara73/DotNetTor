using System;
using System.IO;
using System.Text;

namespace DotNetTor.SocksPort.Helpers
{
	internal class ReadsToChunksStream : Stream
	{
		private const string LineSeparator = "\r\n";
		private readonly Stream _innerStream;
		private bool _complete;

		public ReadsToChunksStream(Stream innerStream)
		{
			_innerStream = innerStream;
			_complete = false;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;

		public override long Length
		{
			get { throw new NotSupportedException(); }
		}

		public override long Position
		{
			get { throw new NotSupportedException(); }
			set { throw new NotSupportedException(); }
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (count < 6)
			{
				throw new ArgumentOutOfRangeException(nameof(count), "The number of bytes to read must be greater than or equal to 6.");
			}

			if (_complete)
			{
				return 0;
			}

			int maximumPrefixLength = (Hex(count) + LineSeparator).Length;
			int innerCount = count - (maximumPrefixLength + LineSeparator.Length);
			int read = _innerStream.Read(buffer, offset + maximumPrefixLength, innerCount);

			// handle the end of the inner stream
			if (read == 0)
			{
				_complete = true;
				var endBytes = Encoding.ASCII.GetBytes(Hex(0) + LineSeparator + LineSeparator);
				Buffer.BlockCopy(endBytes, 0, buffer, offset, endBytes.Length);
				return endBytes.Length;
			}

			// write the prefix
			var prefixBytes = Encoding.ASCII.GetBytes(Hex(read) + LineSeparator);
			Buffer.BlockCopy(prefixBytes, 0, buffer, offset, prefixBytes.Length);

			if (prefixBytes.Length < maximumPrefixLength)
			{
				// we need to shift the chunk
				// TODO: is it faster to Buffer.BlockCopy to a secondary array?
				for (int i = 0; i < read; i++)
				{
					buffer[offset + prefixBytes.Length + i] = buffer[offset + maximumPrefixLength + i];
				}
			}

			// write the suffix
			Buffer.BlockCopy(Encoding.ASCII.GetBytes(LineSeparator), 0, buffer, offset + prefixBytes.Length + read, 2);

			// TODO: possible optimization is to immediately write the "0\r\n\r\n" if there is enough room left

			return prefixBytes.Length + read + 2;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		private string Hex(int i)
		{
			return Convert.ToString(i, 16);
		}
	}
}