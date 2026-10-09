using OmegaCloud.Application.Common.Exceptions;

namespace OmegaCloud.Application.Common.Helpers;

public class SizeLimitingStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _maxBytes;
    private long _bytesRead;

    public SizeLimitingStream(Stream innerStream, long maxBytes)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _maxBytes = maxBytes;
    }

    public long BytesRead => _bytesRead;

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _innerStream.CanSeek ? _innerStream.Length : _bytesRead;
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _innerStream.Read(buffer, offset, count);
        CheckLimit(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        CheckLimit(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _innerStream.ReadAsync(buffer, cancellationToken);
        CheckLimit(read);
        return read;
    }

    private void CheckLimit(int read)
    {
        _bytesRead += read;
        if (_bytesRead > _maxBytes)
        {
            throw new ValidationException($"Stream exceeded the maximum allowed size of {_maxBytes} bytes (50 MB). Use chunked upload sessions for larger files.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
