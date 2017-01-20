using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Helpers
{
	internal sealed class LimitedStream : Stream
	{
		private readonly Stream _innerStream;
		private bool _disposed;
		private long _length;
		private readonly bool _leaveInnerStreamOpen;

		public LimitedStream(Stream innerStream, long length, bool leaveOpen)
		{
			_innerStream = innerStream;
			_disposed = false;
			_leaveInnerStreamOpen = leaveOpen;
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
				if(!_leaveInnerStreamOpen)
					_innerStream.Dispose();
				_disposed = true;
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
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