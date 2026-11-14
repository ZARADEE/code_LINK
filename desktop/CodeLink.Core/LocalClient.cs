using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json.Nodes;
namespace CodeLink;
public sealed class LocalClient : IDisposable
{
    private NamedPipeClientStream? pipe;
    private readonly SemaphoreSlim writeLock=new(1);
    private readonly ConcurrentDictionary<string,TaskCompletionSource<JsonNode>> pending=new();
    public event Action<JsonNode>? State;
    public event Action? Disconnected;
    public JsonNode? CurrentState {get;private set;}
    public async Task Connect(int timeout=2000)
    {
        pipe=new NamedPipeClientStream(".",Broker.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        try{await pipe.ConnectAsync(timeout);}catch{pipe.Dispose();throw;}
        await Wire.Write(pipe,new{type="hello"},writeLock);
        var welcome=await Wire.Read(pipe);if(welcome?.S("type")!="welcome")throw new IOException("本地代理握手失败");
        var connected=pipe;
        _=Task.Run(async()=>{try{while(await Wire.Read(connected) is { } message){if(message.S("type")=="state"){CurrentState=message;State?.Invoke(message);}else if(message.S("type")=="result"&&pending.TryRemove(message.S("id"),out var tcs))tcs.TrySetResult(message["result"]!);}}catch{}finally{foreach(var tcs in pending.Values)tcs.TrySetException(new IOException("本地代理连接断开"));pending.Clear();Disconnected?.Invoke();}});
    }
    public Task Watch(string sessionId)=>Wire.Write(pipe!,new{type="watch",sessionId},writeLock);
    public async Task<JsonNode> Action(string action,object? parameters=null)
    {
        var id=Guid.NewGuid().ToString("N");var msg=parameters is null?new JsonObject():J.Node(parameters).AsObject();msg["type"]="action";msg["action"]=action;msg["id"]=id;
        var tcs=new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=tcs;
        try{await Wire.Write(pipe!,msg,writeLock);var result=await tcs.Task.WaitAsync(TimeSpan.FromSeconds(100));if(!result.B("ok"))throw new IOException(result.S("error","操作失败"));return result;}finally{pending.TryRemove(id,out _);}
    }
    public void Dispose(){pipe?.Dispose();}
}
