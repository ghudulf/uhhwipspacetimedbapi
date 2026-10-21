namespace TicketSalesApp.Services.Diagnostics;

/// <summary>A bounded tee: the original stream always receives every byte; only the prefix is retained.</summary>
public sealed class TrafficCaptureStream(Stream inner, int cap, Func<bool>? capture = null) : Stream
{
    private readonly MemoryStream preview = new();
    public long TotalBytes { get; private set; }
    public int BufferedBytes => checked((int)preview.Length);
    public byte[] Preview => preview.ToArray();
    private void Observe(ReadOnlySpan<byte> bytes)
    {
        TotalBytes += bytes.Length;
        if (capture?.Invoke() == false) return;
        var count = Math.Min(bytes.Length, Math.Max(0, cap - BufferedBytes));
        preview.Write(bytes[..count]);
    }
    public override void Write(byte[] buffer, int offset, int count) { Observe(buffer.AsSpan(offset, count)); inner.Write(buffer, offset, count); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { Observe(buffer.Span); await inner.WriteAsync(buffer, cancellationToken); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) preview.Dispose(); base.Dispose(disposing); }
}

/// <summary>Replays a capped peek and then streams the remainder without buffering it.</summary>
public sealed class TrafficReplayStream(byte[] prefix, Stream remainder, bool leaveOpen = false) : Stream
{
    private int position;
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (position >= prefix.Length) return remainder.Read(buffer, offset, count);
        var size = Math.Min(count, prefix.Length - position);
        prefix.AsSpan(position, size).CopyTo(buffer.AsSpan(offset, size)); position += size; return size;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position >= prefix.Length) return await remainder.ReadAsync(buffer, cancellationToken);
        var size = Math.Min(buffer.Length, prefix.Length - position);
        prefix.AsMemory(position, size).CopyTo(buffer); position += size; return size;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing) { if (disposing && !leaveOpen) remainder.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
