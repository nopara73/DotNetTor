using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Helpers
{
	internal class LimitedStream : Stream
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

		public override bool CanRead
		{
			get { return true; }
		}

		public override bool CanSeek
		{
			get { return false; }
		}

		public override bool CanWrite
		{
			get { return false; }
		}

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
				_innerStream.Dispose();
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

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			var limitedCount = (int)Math.Min(_length, count);
			if (limitedCount == 0)
			{
				return 0;
			}

			var read = await _innerStream.ReadAsync(buffer, offset, limitedCount, cancellationToken).ConfigureAwait(false);
			_length -= read;
			return read;
		}
	}
}