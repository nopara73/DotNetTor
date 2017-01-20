using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Helpers
{
	internal class ByteStreamReader : IDisposable
	{
		private readonly Stream _stream;
		private readonly bool _preserveLineEndings;
		private readonly Encoding _encoding;
		private readonly string _lineEnding;
		private readonly byte[] _lineEndingBuffer;
		private readonly byte[] _buffer;

		private bool _disposed;
		private int _position;
		private int _bufferSize;

		public ByteStreamReader(Stream stream, int bufferSize, bool preserveLineEndings)
		{
			_stream = stream;
			_preserveLineEndings = preserveLineEndings;
			_encoding = new UTF8Encoding(false);
			_lineEnding = "\r\n";
			_lineEndingBuffer = _encoding.GetBytes("\r\n");
			_buffer = new byte[bufferSize];

			_disposed = false;
			_position = 0;
			_bufferSize = -1;
		}

		public Stream GetRemainingStream()
		{
			return new PartiallyBufferedStream(_buffer, _position, _bufferSize - _position, _stream);
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_stream.Dispose();
				_disposed = true;
			}
		}

		public async Task<string> ReadLineAsync()
		{
			// Ensure first read
			if (_bufferSize < 0)
			{
				_bufferSize = await _stream.ReadAsync(_buffer, 0, _buffer.Length).ConfigureAwait(false);
			}

			if (_bufferSize == 0)
			{
				return null;
			}

			var lineStream = new MemoryStream();
			int lineEndingPosition = 0;
			bool lineFinished = false;
			while (lineEndingPosition < _lineEndingBuffer.Length && _bufferSize > 0)
			{
				int endPosition;
				for (endPosition = _position; endPosition < _bufferSize; endPosition++)
				{
					if (_buffer[endPosition] == _lineEndingBuffer[lineEndingPosition])
					{
						lineEndingPosition++;
						if (lineEndingPosition == _lineEndingBuffer.Length)
						{
							endPosition++;
							lineFinished = true;
							break;
						}
					}
					else if (lineEndingPosition > 0)
					{
						lineEndingPosition = 0;
					}
				}
				await lineStream.WriteAsync(_buffer, _position, endPosition - _position).ConfigureAwait(false);
				_position = endPosition;

				if (endPosition == _bufferSize && !lineFinished)
				{
					_bufferSize = await _stream.ReadAsync(_buffer, 0, _buffer.Length).ConfigureAwait(false);
					_position = 0;
				}
			}
			ArraySegment<byte> buffer;
			if (!lineStream.TryGetBuffer(out buffer))
				throw new Exception("Can't get buffer");

			var line = _encoding.GetString(buffer.ToArray(), 0, (int)lineStream.Length);
			if (!_preserveLineEndings && lineFinished)
			{
				line = line.Substring(0, line.Length - _lineEnding.Length);
			}

			return line;
		}

		public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
		{
			int read = 0;
			if (_bufferSize >= 0)
			{
				read = Math.Min(count, _bufferSize - _position);
				Buffer.BlockCopy(_buffer, _position, buffer, offset, read);
				count -= read;
				offset += read;
				_position += read;

				if (_position == _bufferSize)
				{
					_bufferSize = -1;
				}
			}

			if (count != 0)
			{
				read += await _stream.ReadAsync(buffer, offset, count).ConfigureAwait(false);
			}

			return read;
		}
	}
}