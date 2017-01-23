using System;
using System.IO;

namespace DotNetTor.SocksPort.Helpers
{
	internal sealed class LimitedStream : Stream
	{
		private readonly Stream _innerStream;
		private bool _disposed;
		private long _length;

		public LimitedStream(Stream innerStream, long length)
		{
			_innerStream = innerStream;
			_disposed = false;
			_length = length;
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

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (!_disposed && disposing)
			{
				_disposed = true;
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var limitedCount = (int)Math.Min(_length, count);
			if (limitedCount == 0)
			{
				return 0;
			}

			var read = _innerStream.Read(buffer, offset, limitedCount);
			_length -= read;
			return read;
		}
	}
}