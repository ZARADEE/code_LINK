using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace CodeLink;
public static class Wire
{
    public const int MaxFrame = 1024*1024;
    public static async Task<JsonNode?> Read(Stream stream, CancellationToken token=default)
    {
        var header=new byte[4]; var n=await stream.ReadAsync(header.AsMemory(0,4),token);
        if(n==0)return null;
        if(n<4)await stream.ReadExactlyAsync(header.AsMemory(n),token);
        var length=BinaryPrimitives.ReadInt32BigEndian(header);
        if(length<2||length>MaxFrame)throw new IOException("消息长度超出限制");
        var bytes=new byte[length]; await stream.ReadExactlyAsync(bytes,token);
        return JsonNode.Parse(bytes)??throw new IOException("无效消息");
    }
    public static async Task Write(Stream stream, object message, SemaphoreSlim writeLock, CancellationToken token=default)
    {
        var bytes=System.Text.Encoding.UTF8.GetBytes(J.Stringify(message));
        if(bytes.Length>MaxFrame)throw new IOException("消息过大");
        var header=new byte[4];BinaryPrimitives.WriteInt32BigEndian(header,bytes.Length);
        await writeLock.WaitAsync(token);
        try { await stream.WriteAsync(header,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token); }
        finally { writeLock.Release(); }
    }
}
