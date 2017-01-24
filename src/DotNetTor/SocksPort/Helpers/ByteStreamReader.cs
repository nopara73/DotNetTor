using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.SocksPort.Helpers
{
	internal sealed class ByteStreamReader : IDisposable
	{
		private readonly Stream _stream;
		private readonly bool _preserveLineEndings;
		private static readonly Encoding Encoding = new UTF8Encoding(false);
		private const string LineEnding = "\r\n";
		private readonly byte[] _lineEndingBuffer = Encoding.GetBytes(LineEnding);
		private readonly byte[] _buffer;

		private bool _disposed = false;
		private int _position = 0;
		private int _bufferSize = -1;

		public ByteStreamReader(Stream stream, int bufferSize, bool preserveLineEndings)
		{
			_stream = stream;
			_preserveLineEndings = preserveLineEndings;
			_buffer = new byte[bufferSize];
		}

		public Stream RemainingStream => new PartiallyBufferedStream(_buffer, _position, _bufferSize - _position, _stream);

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
			}
		}

		public string ReadLine()
		{
			// Ensure first read
			if (_bufferSize < 0)
			{
				ReadWaitRetry();
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

				lineStream.Write(_buffer, _position, endPosition - _position);
				_position = endPosition;

				if (endPosition == _bufferSize && !lineFinished)
				{
					ReadWaitRetry();
					_position = 0;
				}
			}

			ArraySegment<byte> buffer;
			if (!lineStream.TryGetBuffer(out buffer))
				throw new Exception("Can't get buffer");

			var line = Encoding.GetString(buffer.ToArray(), 0, (int)lineStream.Length);
			if (!_preserveLineEndings && lineFinished)
			{
				line = line.Substring(0, line.Length - LineEnding.Length);
			}

			return line;
		}
		private const int MaxTry = 7;
		private int _tried = 0;
		private void ReadWaitRetry()
		{
			try
			{
				_bufferSize = _stream.Read(_buffer, 0, _buffer.Length);
				_tried = 0;
			}
			catch (NotSupportedException)
			{
				if (_tried < MaxTry)
					_tried++;
				else throw;

				Task.Delay(50).Wait();
				ReadWaitRetry();
			}
		}

		public int Read(byte[] buffer, int offset, int count)
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
				read += _stream.Read(buffer, offset, count);
			}

			return read;
		}
	}
}