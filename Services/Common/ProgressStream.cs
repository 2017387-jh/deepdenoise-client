using System.IO;
using System.Net.Http;

namespace DeepDenoiseClient.Services.Common;

public sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly IProgress<long>? _progress;
    private long _transferred;

    public ProgressStream(Stream inner, IProgress<long>? progress)
    { _inner = inner; _progress = progress; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        _transferred += read;
        _progress?.Report(_transferred);
        return read;
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        _transferred += count;
        _progress?.Report(_transferred);
    }
}