using CodeLink;
using System.Text.Json.Nodes;

RuntimeConfig.Load();
ConsoleTerminal.Enable();
try { await Main(); }
catch(Exception e){Console.Error.WriteLine("Code Link: "+e.Message);Environment.ExitCode=1;}

async Task Main()
{
    if(args.Length==0||args.Contains("--help"))
    {
        Console.WriteLine("""
        clink terminal [--cwd PATH] [--name NAME] [--new-window] [-- PROGRAM ARGS...]
        clink codex [--cwd PATH] [--name NAME] [--new-window]
        clink attach ID | clink list | clink close ID | clink scan

        terminal: 在当前窗口使用真实终端。Ctrl+] 离开前端，任务继续。
        --new-window: 打开新的可操作命令行窗口。
        codex: 使用本机 Codex，引擎审批与完成消息同步到平板。
        """);return;
    }
    var mode=args[0];var cwd=Environment.CurrentDirectory;string? name=null;JsonNode? config=null;
    string command="powershell.exe -NoLogo";
    for(var i=1;i<args.Length;i++)
    {
        if(args[i]=="--"){command=string.Join(" ",args.Skip(i+1).Select(ConsoleTerminal.Quote));break;}
        if(args[i]=="--cwd"){cwd=Path.GetFullPath(args[++i]);continue;}
        if(args[i]=="--name"){name=args[++i];continue;}
        if(args[i]=="--config"){config=JsonNode.Parse(File.ReadAllText(args[++i]));continue;}
        if(args[i]=="--new-window")continue;
        if(mode is "terminal" or "codex"){if(i==1&&!args[i].StartsWith("--"))cwd=Path.GetFullPath(args[i]);else throw new ArgumentException("未知参数 "+args[i]);}
    }
    if(!Directory.Exists(cwd))throw new IOException("工作目录不存在："+cwd);
    if(args.Contains("--new-window")){ConsoleTerminal.NewWindow(args.Where(a=>a!="--new-window").ToArray(),cwd);return;}
    if(mode=="scan"){Console.WriteLine(J.Stringify(CommandCatalog.Scan()));return;}
    if(mode is not ("terminal" or "codex" or "attach" or "list" or "close"))throw new ArgumentException("未知命令，请运行 clink --help");

    using var client=new LocalClient();
    await HostLauncher.Connect(client);
    var deadline=DateTime.UtcNow.AddSeconds(5);
    while(client.CurrentState is null){if(DateTime.UtcNow>deadline)throw new TimeoutException("未收到代理状态");await Task.Delay(30);}
    if(mode=="list"){Console.WriteLine(client.CurrentState["sessions"]?.ToJsonString());return;}
    if(mode=="close"){await client.Action("delete",new{sessionId=args.ElementAtOrDefault(1)??""});return;}
    string sessionId,kind;
    if(mode=="attach")
    {
        sessionId=args.ElementAtOrDefault(1)??throw new ArgumentException("缺少 ID");
        var result=await client.Action("attach",new{sessionId,frontendPid=Environment.ProcessId});kind=result.S("kind");
    }
    else
    {
        var result=await client.Action("create",new{kind=mode,cwd,name=name??new DirectoryInfo(cwd).Name,command,frontendPid=Environment.ProcessId,config});
        sessionId=result.S("sessionId");kind=mode;
    }
    Console.Title="Code Link | "+(name??new DirectoryInfo(cwd).Name)+" | "+sessionId[..8];
    using var stop=new CancellationTokenSource();
    client.Disconnected+=()=>{Console.Error.WriteLine("\n代理已断开");stop.Cancel();};
    var knownRequests=new HashSet<string>();
    client.State+=state=>
    {
        if(state.S("selected")!=sessionId)return;
        if(state.B("reset")&&!Console.IsOutputRedirected)Console.Write("\u001b[2J\u001b[H");
        Console.Write(state.S("output"));
        var current=state["sessions"]?.AsArray().FirstOrDefault(s=>s.S("id")==sessionId);
        if(kind=="terminal"&&current.S("status") is "exited" or "completed" or "failed")stop.Cancel();
        if(current?["approvals"] is JsonArray requests)
            foreach(var request in requests)
                if(request is not null&&knownRequests.Add(request.S("id")))Console.WriteLine("\n[需要处理] "+request.S("summary")+"\n/allow "+request.S("id")+"  或 /deny "+request.S("id"));
    };
    await client.Watch(sessionId);
    if(kind=="terminal"&&!Console.IsInputRedirected)
    {
        Console.TreatControlCAsInput=true;var dimensions=(0,0);
        while(!stop.IsCancellationRequested)
        {
            var size=(Console.WindowWidth,Console.WindowHeight);
            if(dimensions!=size){dimensions=size;await client.Action("resize",new{sessionId,cols=size.Item1,rows=size.Item2});}
            if(Console.KeyAvailable)
            {
                var key=Console.ReadKey(true);if(key.KeyChar=='\u001d')break;
                var value=ConsoleTerminal.Keys(key);if(value.Length>0)await client.Action("input",new{sessionId,text=value,raw=true});
            }
            else await Task.Delay(10);
        }
        Console.TreatControlCAsInput=false;Console.Write("\u001b[0m");return;
    }
    if(kind=="codex")Console.WriteLine("\n/quit 离开前端  /interrupt 中断  /requests 查看请求  /allow ID  /deny ID  /answer ID JSON\n");
    Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;_=client.Action("interrupt",new{sessionId});};
    while(!stop.IsCancellationRequested&&await Console.In.ReadLineAsync() is {} line)
    {
        if(line=="/quit")break;
        try
        {
            if(line=="/interrupt"){await client.Action("interrupt",new{sessionId});continue;}
            if(line=="/requests"){Console.WriteLine(client.CurrentState?["sessions"]?.AsArray().FirstOrDefault(s=>s.S("id")==sessionId)?["approvals"]?.ToJsonString());continue;}
            if(line.StartsWith("/allow ")||line.StartsWith("/deny "))
            {await client.Action("approve",new{sessionId,requestId=line.Split(' ',2)[1],decision=line.StartsWith("/allow")?"accept":"decline"});continue;}
            if(line.StartsWith("/answer "))
            {var parts=line.Split(' ',3);await client.Action("approve",new{sessionId,requestId=parts[1],response=JsonNode.Parse(parts[2])});continue;}
            if(line.Length>0)await client.Action("input",new{sessionId,text=line});
        }
        catch(Exception e){Console.Error.WriteLine("["+e.Message+"]");}
    }
}
