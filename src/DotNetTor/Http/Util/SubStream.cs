
//-----------------------------------------------------------------------
// <copyright file="SubStream.cs" company="Microsoft">
//    Copyright 2016 Microsoft Corporation
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using System.Threading;

namespace System.IO
{
	/// <summary>
	/// A wrapper class that creates a logical substream from a region within an existing seekable stream.
	/// Allows for concurrent, asynchronous read and seek operations on the wrapped stream.
	/// This class will buffer read requests to minimize overhead on the underlying stream.
	/// </summary>
	public sealed class SubStream : Stream
	{
		// Stream to be logically wrapped.
		private Stream _wrappedStream;

		// Position in the wrapped stream at which the SubStream should logically begin.
		private long _streamBeginIndex;

		// Total length of the substream.
		private long _substreamLength;

		// Tracks the current position in the substream.
		private long _substreamCurrentIndex;

		// Stream to manage read buffer, lazily initialized when read or seek operations commence.
		private Lazy<MemoryStream> _readBufferStream;

		// Internal read buffer, lazily initialized when read or seek operations commence.
		private Lazy<byte[]> _readBuffer;

		// Tracks the valid bytes remaining in the readBuffer
		private int _readBufferLength;

		// Determines where to update the position of the readbuffer stream depending on the scenario)
		private bool _shouldSeek = false;

		// Current relative position in the substream.
		public override long Position
		{
			get
			{
				return _substreamCurrentIndex;
			}

			set
			{
				// Check if we can potentially advance substream position without reallocating the read buffer.
				if (value >= _substreamCurrentIndex)
				{
					long offset = value - _substreamCurrentIndex;

					// New position is within the valid bytes stored in the readBuffer.
					if (offset <= _readBufferLength)
					{
						_readBufferLength -= (int)offset;
						if (_shouldSeek)
						{
							_readBufferStream.Value.Seek(offset, SeekOrigin.Current);
						}
					}
					else
					{
						// Resets the read buffer.
						_readBufferLength = 0;
						_readBufferStream.Value.Seek(0, SeekOrigin.End);
					}
				}
				else
				{
					// Resets the read buffer.
					_readBufferLength = 0;
					_readBufferStream.Value.Seek(0, SeekOrigin.End);
				}

				_substreamCurrentIndex = value;
			}
		}

		// Total length of the substream.
		public override long Length => _substreamLength;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		private void CheckDisposed()
		{
			if (_wrappedStream == null)
			{
				throw new ObjectDisposedException("SubStreamWrapper");
			}
		}

		protected override void Dispose(bool disposing)
		{
			_wrappedStream = null;
			_readBufferStream = null;
			_readBuffer = null;
		}

		public override void Flush() => throw new NotSupportedException();

		// Initiates the new buffer size to be used for read operations.
		public int ReadBufferSize
		{
			get
			{
				return _readBuffer.Value.Length;
			}

			set
			{
				_readBuffer = new Lazy<byte[]>(() => new byte[value]);
				_readBufferStream = new Lazy<MemoryStream>(() => new MemoryStream(_readBuffer.Value, 0, value, true));
				_readBufferStream.Value.Seek(0, SeekOrigin.End);
			}
		}

		/// <summary>
		/// Creates a new SubStream instance.
		/// </summary>
		/// <param name="stream">A seekable source stream.</param>
		/// <param name="streamBeginIndex">The index in the wrapped stream where the logical SubStream should begin.</param>
		/// <param name="substreamLength">The length of the SubStream.</param>
		/// <remarks>
		/// The source stream to be wrapped must be seekable.
		/// The Semaphore object provided must have the initialCount thread parameter set to one to ensure only one concurrent request is granted at a time.
		/// </remarks>
		public SubStream(Stream stream, long streamBeginIndex, long substreamLength)
		{
			_streamBeginIndex = streamBeginIndex;
			_wrappedStream = stream ?? throw new ArgumentNullException("Stream.");
			_substreamLength = substreamLength;
			_readBufferLength = 0;
			Position = 0;
			ReadBufferSize = 1024;
		}

		/// <summary>
		/// Reads a block of bytes asynchronously from the substream read buffer.
		/// </summary>
		/// <param name="buffer">When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
		/// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
		/// <param name="count">The maximum number of bytes to be read.</param>
		/// <param name="cancellationToken">An object of type <see cref="CancellationToken"/> that propagates notification that operation should be canceled.</param>
		/// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero if the end of the substream has been reached.</returns>
		/// <remarks>
		/// If the read request cannot be satisfied because the read buffer is empty or contains less than the requested number of the bytes, 
		/// the wrapped stream will be called to refill the read buffer.
		/// Only one read request to the underlying wrapped stream will be allowed at a time and concurrent requests will be queued up by effect of the shared semaphore mutex.
		/// </remarks>
		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			CheckDisposed();

			try
			{
				int readCount = CheckAdjustReadCount(count, offset, buffer.Length);
				int bytesRead = await _readBufferStream.Value.ReadAsync(buffer, offset, Math.Min(readCount, _readBufferLength), cancellationToken).ConfigureAwait(false);
				int bytesLeft = readCount - bytesRead;

				// must adjust readbufferLength
				_shouldSeek = false;
				Position += bytesRead;

				if (bytesLeft > 0 && _readBufferLength == 0)
				{
					_readBufferStream.Value.Position = 0;
					int bytesAdded =
						await ReadAsyncHelper(_readBuffer.Value, 0, _readBuffer.Value.Length, cancellationToken).ConfigureAwait(false);
					_readBufferLength = bytesAdded;
					if (bytesAdded > 0)
					{
						bytesLeft = Math.Min(bytesAdded, bytesLeft);
						int secondRead = await _readBufferStream.Value.ReadAsync(buffer, bytesRead + offset, bytesLeft, cancellationToken).ConfigureAwait(false);
						bytesRead += secondRead;
						Position += secondRead;
					}
				}

				return bytesRead;
			}
			finally
			{
				_shouldSeek = true;
			}
		}

		/// <summary>
		/// Reads a block of bytes from the wrapped stream asynchronously and writes the data to the SubStream buffer.
		/// </summary>
		/// <param name="buffer">When this method returns, the substream read buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
		/// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
		/// <param name="count">The maximum number of bytes to be read.</param>
		/// <param name="cancellationToken">An object of type <see cref="CancellationToken"/> that propagates notification that operation should be canceled.</param>
		/// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero if the end of the substream has been reached.</returns>
		/// <remarks>
		/// This method will allow only one read request to the underlying wrapped stream at a time, 
		/// while concurrent requests will be queued up by effect of the shared semaphore mutex.
		/// The caller is responsible for adjusting the substream position after a successful read.
		/// </remarks>
		private async Task<int> ReadAsyncHelper(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			int result = 0;

			CheckDisposed();

			// Check if read is out of range and adjust to read only up to the substream bounds.
			count = CheckAdjustReadCount(count, offset, buffer.Length);

			// Only seek if wrapped stream is misaligned with the substream position.
			if (_wrappedStream.Position != _streamBeginIndex + Position)
			{
				_wrappedStream.Seek(_streamBeginIndex + Position, SeekOrigin.Begin);
			}

			result = await _wrappedStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

			return result;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return ReadAsync(buffer, offset, count).Result;
		}

		/// <summary>
		/// Sets the position within the current substream. 
		/// This operation does not perform a seek on the wrapped stream.
		/// </summary>
		/// <param name="offset">A byte offset relative to the origin parameter.</param>
		/// <param name="origin">A value of type System.IO.SeekOrigin indicating the reference point used to obtain the new position.</param>
		/// <returns>The new position within the current substream.</returns>
		/// <exception cref="NotSupportedException">Thrown if using the unsupported <paramref name="origin"/> SeekOrigin.End </exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="offset"/> is invalid for SeekOrigin.</exception>
		public override long Seek(long offset, SeekOrigin origin)
		{
			CheckDisposed();
			long startIndex;

			// Map offset to the specified SeekOrigin of the substream.
			switch (origin)
			{
				case SeekOrigin.Begin:
					startIndex = 0;
					break;

				case SeekOrigin.Current:
					startIndex = Position;
					break;

				case SeekOrigin.End:
					throw new NotSupportedException();

				default:
					throw new ArgumentOutOfRangeException();
			}

			Position = startIndex + offset;
			return Position;
		}

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		private int CheckAdjustReadCount(int count, int offset, int bufferLength)
		{
			if (offset < 0 || count < 0 || offset + count > bufferLength)
			{
				throw new ArgumentOutOfRangeException();
			}

			long currentPos = _streamBeginIndex + Position;
			long endPos = _streamBeginIndex + _substreamLength;
			if (currentPos + count > endPos)
			{
				return (int)(endPos - currentPos);
			}
			else
			{
				return count;
			}
		}
	}
}
