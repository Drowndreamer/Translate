namespace Translate.Core;
// Enforce a byte budget before StreamReader or the JSON parser can accumulate an
// unbounded response, including a single SSE line without a newline.
internal sealed class ResponseBudgetStream(Stream inner) : Stream
{
    private long _read;
    private int Count(int count)
    {
        _read+=count;
        if(_read>TranslationLimits.ResponseBytes)throw new TranslationException("接口响应过大，请缩短原文或检查 API 地址。");
        return count;
    }
    public override bool CanRead=>inner.CanRead;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>Count(inner.Read(buffer,offset,count));
    public override int Read(Span<byte> buffer)=>Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)=>Count(await inner.ReadAsync(buffer,cancellationToken).ConfigureAwait(false));
    public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellationToken)=>Count(await inner.ReadAsync(buffer.AsMemory(offset,count),cancellationToken).ConfigureAwait(false));
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
}
