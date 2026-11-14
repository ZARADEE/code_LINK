using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace CodeLink;
public sealed class CodexEngine : IDisposable
{
    private Process? process;
    private readonly ConcurrentDictionary<string,TaskCompletionSource<JsonNode>> pending=new();
    private readonly SemaphoreSlim writer=new(1);
    private readonly SemaphoreSlim starter=new(1);
    private long next;
    public event Action<JsonNode>? Message;
    public event Action<string>? Stderr;
    public event Action? Exited;
    public async Task Start(string configuredPath)
    {
        await starter.WaitAsync();
        try
        {
            if(process is {HasExited:false})return;
            var executable=configuredPath;
            if(string.IsNullOrWhiteSpace(executable))
            {
                var npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai");
                var candidates=new[]{ Path.Combine(npm,"codex","node_modules","@openai","codex-win32-x64","vendor","x86_64-pc-windows-msvc","bin","codex.exe"),Path.Combine(npm,"codex-win32-x64","vendor","x86_64-pc-windows-msvc","bin","codex.exe")};
                executable=candidates.FirstOrDefault(File.Exists)??throw new FileNotFoundException("未找到 Codex。请在 PC 设置中填写 codex.exe 路径。");
            }
            var info=new ProcessStartInfo(executable){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,
                StandardInputEncoding=new System.Text.UTF8Encoding(false),StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
            info.ArgumentList.Add("app-server");info.ArgumentList.Add("--listen");info.ArgumentList.Add("stdio://");
            process=Process.Start(info)??throw new IOException("Codex 启动失败");
            var current=process;
            _=Task.Run(async()=>{try{while(await current.StandardOutput.ReadLineAsync() is { } line){JsonNode? node;try{node=JsonNode.Parse(line);}catch{continue;}if(node is null)continue;
                if(node["id"] is not null&&node["method"] is null&&pending.TryRemove(node["id"]!.ToString(),out var completion)){if(node["error"] is not null)completion.TrySetException(new IOException(node["error"]!.ToJsonString()));else completion.TrySetResult(node["result"]??new JsonObject());}
                else Message?.Invoke(node);
            }}finally{foreach(var p in pending.Values)p.TrySetException(new IOException("Codex 连接已结束"));pending.Clear();Exited?.Invoke();}});
            _=Task.Run(async()=>{while(await current.StandardError.ReadLineAsync() is { } line)Stderr?.Invoke(line);});
            await Call("initialize",new{clientInfo=new{name="code_link",title="Code Link",version="1.0.0"},capabilities=new{experimentalApi=true}});
            await Send(new {method="initialized",@params=new{}});
        }
        finally {starter.Release();}
    }
    public async Task<JsonNode> Call(string method, object parameters)
    {
        var id="cl-"+Interlocked.Increment(ref next);
        var tcs=new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=tcs;
        try{await Send(new{id,method,@params=parameters});return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(90));}
        finally{pending.TryRemove(id,out _);}
    }
    public async Task Send(object message)
    {
        await writer.WaitAsync();try{if(process is null||process.HasExited)throw new IOException("Codex 未启动");await process.StandardInput.WriteLineAsync(J.Stringify(message));await process.StandardInput.FlushAsync();}finally{writer.Release();}
    }
    public void Dispose(){try{process?.StandardInput.Close();if(process is {HasExited:false})process.Kill(true);}catch{}process?.Dispose();}
}
