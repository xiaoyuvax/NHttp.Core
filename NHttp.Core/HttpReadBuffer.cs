using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NHttp
{
    internal class HttpReadBuffer
    {
        private readonly int _bufferSize;
        private int _available;
        private byte[] _buffer;
        private bool _forceNewRead;
        private StringBuilder _lineBuffer;
        private int _offset;
        private int _totalRead;
        public HttpReadBuffer(int size)
        {
            _buffer = new byte[_bufferSize = size];
        }

        public bool DataAvailable => !_forceNewRead && _offset < _available;
        public bool? AtBoundary(byte[] boundary, int maximum)
        {
            if (boundary == null)
                throw new ArgumentNullException("boundary");

            if (maximum - _totalRead < boundary.Length)
                throw new ProtocolException("Not enough data available for multipart boundary to match");

            if (_available - _offset < boundary.Length)
                return null;

            for (int i = 0; i < boundary.Length; i++)
            {
                if (boundary[i] != _buffer[i + _offset])
                    return false;
            }

            _offset += boundary.Length;
            _totalRead += boundary.Length;

            return true;
        }

        public void CopyToStream(Stream stream, int maximum) => CopyToStream(stream, maximum, null);

        public bool CopyToStream(Stream stream, int maximum, byte[] boundary)
        {
            int toRead = Math.Min(_available - _offset, maximum - _totalRead);

            bool atBoundary = false;
            bool partialBoundaryMatch = false;

            if (boundary != null)
            {
                int boundaryOffset = -1;

                // Use Span<byte> for more efficient boundary scanning
                ReadOnlySpan<byte> bufferSpan = new ReadOnlySpan<byte>(_buffer, _offset, toRead);
                ReadOnlySpan<byte> boundarySpan = new ReadOnlySpan<byte>(boundary);

                for (int i = 0; i <= toRead - boundary.Length; i++)
                {
                    if (bufferSpan.Slice(i, boundary.Length).SequenceEqual(boundarySpan))
                    {
                        boundaryOffset = i;
                        break;
                    }
                }

                // Check for partial boundary match at the end
                if (boundaryOffset == -1 && toRead > 0)
                {
                    int possibleMatchLength = Math.Min(boundary.Length - 1, toRead);
                    for (int i = 1; i <= possibleMatchLength; i++)
                    {
                        if (bufferSpan.Slice(toRead - i, i).SequenceEqual(boundarySpan.Slice(0, i)))
                        {
                            partialBoundaryMatch = true;
                            break;
                        }
                    }
                }

                if (boundaryOffset != -1)
                {
                    // If we have a boundary, we can read up until the boundary offset.

                    toRead = boundaryOffset;
                    atBoundary = !partialBoundaryMatch;

                    // When we have a partial boundary match, we need more data.

                    if (partialBoundaryMatch) _forceNewRead = true;
                }
            }

            stream.Write(_buffer, _offset, toRead);

            _offset += toRead;
            _totalRead += toRead;

            // If we found the boundary, we also skip it.

            if (atBoundary)
            {
                _offset += boundary.Length;
                _totalRead += boundary.Length;
            }
            else if (boundary != null && maximum - _totalRead < boundary.Length)
            {
                throw new ProtocolException("Not enough data available for multipart boundary to match");
            }

            return atBoundary;
        }

        public void EndRead(Task<int> asyncResult) => _available += asyncResult.Result;

        public Task<int> ReadAsync(Stream stream)
        {
            // A new read was requested. Reset the flag.

            _forceNewRead = false;

            if (_offset == _available)
            {
                // If the offset is at the end, we can just reset the
                // positions.

                _offset = 0;
                _available = 0;
            }
            else if (_buffer.Length - _available < _bufferSize)
            {
                // If there is less than the initial buffer size room left,
                // we need to move some data.

                if (_buffer.Length - (_available - _offset) < _bufferSize)
                {
                    // If the available size is less than the initial buffer size,
                    // enlarge the buffer.

                    var buffer = new byte[_buffer.Length * 2];

                    // Copy the unprocessed bytes to the start of the new buffer.

                    Array.Copy(_buffer, _offset, buffer, 0, _available - _offset);

                    _buffer = buffer;
                }
                else
                {
                    // Else, just move the unprocessed bytes to the beginning.

                    Array.Copy(_buffer, _offset, _buffer, 0, _available - _offset);
                }

                // Reset the position and available to reflect the moved
                // bytes.

                _available -= _offset;
                _offset = 0;
            }

            // We don't use the whole buffer, only what we were assigned in
            // the beginning. This is to prevent the read buffer from
            // resizing too much.
            int bufferAvailable = Math.Min(_buffer.Length - _available, _bufferSize);

            Task<int> ar = null;
            lock (stream)
            {
                if (stream != null && stream.CanRead) ar = stream.ReadAsync(_buffer, _available, bufferAvailable);
            }
            return ar;
        }

        public string ReadLine()
        {
            if (_lineBuffer == null) _lineBuffer = new StringBuilder();

            while (_offset < _available)
            {
                int c = _buffer[_offset++];

                _totalRead++;

                if (c == '\n')
                {
                    string line = _lineBuffer.ToString();

                    _lineBuffer.Clear();

                    return line;
                }
                else if (c != '\r')
                {
                    _lineBuffer.Append((char)c);
                }
            }

            return null;
        }

        public void Reset()
        {
            _lineBuffer = null;
            _totalRead = 0;
        }
    }
}