using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Helpers
{
	internal class ReadsFromChunksStream : Stream
	{
		private readonly ByteStreamReader _byteStreamReader;
		private bool _disposed;
		private int _chunkSize;
		private int _remaining;

		public ReadsFromChunksStream(Stream innerStream)
		{
			_byteStreamReader = new ByteStreamReader(innerStream, 4096, false);
			_disposed = false;
			_chunkSize = -1;
			_remaining = -1;
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
				_byteStreamReader.Dispose();
				_disposed = true;
			}
		}
		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_remaining <= 0)
			{
				var line = _byteStreamReader.ReadLine();
				_chunkSize = (int)Convert.ToUInt32(line, 16);
				_remaining = _chunkSize;
			}

			int read = 0;
			if (_remaining > 0)
			{
				int actualCount = Math.Min(count, _remaining);
				read = _byteStreamReader.Read(buffer, offset, actualCount);
				_remaining -= read;
			}

			if (_remaining == 0)
			{
				_byteStreamReader.ReadLine();
			}

			return read;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			if (_remaining <= 0)
			{
				var line = await _byteStreamReader.ReadLineAsync().ConfigureAwait(false);
				_chunkSize = (int)Convert.ToUInt32(line, 16);
				_remaining = _chunkSize;
			}

			int read = 0;
			if (_remaining > 0)
			{
				int actualCount = Math.Min(count, _remaining);
				read = await _byteStreamReader.ReadAsync(buffer, offset, actualCount).ConfigureAwait(false);
				_remaining -= read;
			}

			if (_remaining == 0)
			{
				await _byteStreamReader.ReadLineAsync().ConfigureAwait(false);
			}

			return read;
		}
	}
}
