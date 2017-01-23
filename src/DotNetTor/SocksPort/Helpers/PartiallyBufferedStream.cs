using System;
using System.IO;

namespace DotNetTor.SocksPort.Helpers
{
	internal sealed class PartiallyBufferedStream : Stream
	{
		private readonly byte[] _buffer;
		private bool _disposed;
		private int _offset;
		private int _length;
		private readonly Stream _innerStream;

		public PartiallyBufferedStream(byte[] buffer, int offset, int length, Stream innerStream)
		{
			_buffer = buffer;
			_disposed = false;
			_offset = offset;
			_length = length;
			_innerStream = innerStream;
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

		private bool TryReadBuffer(byte[] buffer, int offset, int count, out int read)
		{
			if (_length > 0)
			{
				read = Math.Min(_length, count);
				Buffer.BlockCopy(_buffer, _offset, buffer, offset, read);
				_length -= read;
				_offset += read;
				return true;
			}

			read = 0;
			return false;
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
			int read;
			if (TryReadBuffer(buffer, offset, count, out read))
			{
				return read;
			}

			return _innerStream.Read(buffer, offset, count);
		}
	}
}