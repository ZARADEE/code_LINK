using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeLink;
public sealed class Broker : IDisposable
{
    public static string PipeName=>"CodeLink.Local.v1"+(Environment.GetEnvironmentVariable("CODELINK_INSTANCE") is {} id?"."+id:"");
    public HostStorage Storage { get; }
    public ConcurrentDictionary<string,Session> Sessions { get; }=new();
    private readonly CodexEngine engine=new();
    private readonly ConcurrentDictionary<string,Peer> peers=new();
    private readonly ConcurrentDictionary<string,Lazy<Task<object>>> actions=new();
    private readonly CancellationTokenSource stop=new();
    private readonly SemaphoreSlim sessionWriter=new(1);
    private TcpListener? listener;
    private BluetoothServer? bluetooth;
    private long version;
    private CommandCandidate[] commandWindows=[];
    private readonly ConcurrentDictionary<string,WindowActivity> windowActivities=new();
    private string pairCode="";
    private DateTime pairExpires;
    private readonly ConcurrentDictionary<string,DateTime> lastFailure=new();
    private static readonly HashSet<string> InteractiveMethods=new(StringComparer.Ordinal)
    {
        "item/commandExecution/requestApproval","item/fileChange/requestApproval","item/permissions/requestApproval",
        "item/tool/requestUserInput","mcpServer/elicitation/request"
    };
    public string BluetoothStatus { get; private set; }="尚未启动";
    public Broker(string data)
    {
        Storage=new(data);RotateCode();
        engine.Message+=OnCodexMessage;
        engine.Exited+=()=>{foreach(var s in Sessions.Values.Where(x=>x.Kind=="codex")){lock(s.Gate){s.Status="disconnected";s.Approvals.Clear();s.InputQueue.Clear();}s.Append("\n[Codex 进程已退出，未自动重放操作]\n");}Changed();};
        LoadSessions();
    }
    public void Changed()=>Interlocked.Increment(ref version);
    public void RotateCode(){pairCode=RandomNumberGenerator.GetInt32(100000,1000000).ToString();pairExpires=DateTime.UtcNow.AddMinutes(10);Changed();}
    public async Task Run()
    {
        listener=new TcpListener(IPAddress.IPv6Any,Storage.Config.Port);
        listener.Server.DualMode=true;listener.Start();
        _=Task.Run(PipeLoop);
        _=Task.Run(BluetoothLoop);
        _=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){
            var excluded=Sessions.Values.SelectMany(s=>new[]{s.Terminal?.ProcessId??0,s.FrontendPid}).Where(id=>id>0).ToHashSet();
            var windows=CommandCatalog.Scan(excluded).Where(p=>p.Kind=="window").ToArray();
            if(!windows.SequenceEqual(commandWindows)){commandWindows=windows;Changed();}
            foreach(var activity in windowActivities.Values)lock(activity){if(activity.Expire(DateTime.UtcNow))Changed();}
            await Task.Delay(3000,stop.Token);
        }}catch(OperationCanceledException){}});
        _=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await Task.Delay(2000,stop.Token);foreach(var s in Sessions.Values.Where(s=>s.Kind=="observer"))lock(s.Gate){if(s.Activity.Expire(DateTime.UtcNow)){s.Status="disconnected";Changed();}}}}catch(OperationCanceledException){}});
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_=Task.Run(()=>HandleTcp(client));}}
        catch(OperationCanceledException){}
    }
    private async Task AcceptTcp4()
    {
        var v4=new TcpListener(IPAddress.Loopback,Storage.Config.Port);v4.Start();
        try{while(!stop.IsCancellationRequested){var c=await v4.AcceptTcpClientAsync(stop.Token);_=Task.Run(()=>HandleTcp(c));}}catch(OperationCanceledException){}finally{v4.Stop();}
    }
    private async Task HandleTcp(TcpClient client)
    {
        using(client)
        using(var ssl=new SslStream(client.GetStream(),false))
        {
            try{if(!Storage.Config.LanEnabled&&!IPAddress.IsLoopback(((IPEndPoint)client.Client.RemoteEndPoint!).Address))return;
                client.NoDelay=true;await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=Storage.Certificate,EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13},stop.Token);
                await Serve(ssl,false,IPAddress.IsLoopback(((IPEndPoint)client.Client.RemoteEndPoint!).Address)?"USB / 本地":"Wi-Fi / 局域网");}
            catch(Exception e) when(e is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or SocketException){}
        }
    }
    private async Task PipeLoop()
    {
        while(!stop.IsCancellationRequested)
        {
            var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,8,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            try{await pipe.WaitForConnectionAsync(stop.Token);_=Task.Run(async()=>{using(pipe)await Serve(pipe,true,"本机");});}
            catch{pipe.Dispose();if(stop.IsCancellationRequested)return;await Task.Delay(500);}
        }
    }
    private async Task BluetoothLoop()
    {
        if(!Storage.Config.BluetoothEnabled){BluetoothStatus="已关闭";return;}
        try
        {
            bluetooth=new BluetoothServer();bluetooth.Start();BluetoothStatus="可连接";Changed();
            while(!stop.IsCancellationRequested){var stream=await Task.Run(bluetooth.Accept,stop.Token);_=Task.Run(async()=>{using(stream)await Serve(stream,false,"蓝牙");});}
        }
        catch(Exception e){BluetoothStatus=e.Message;Changed();}
    }
    private async Task Serve(Stream stream,bool admin,string transport)
    {
        using var peer=new Peer(stream){Admin=admin,Transport=transport};
        using var alive=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var connectionId=Guid.NewGuid().ToString("N");
        try
        {
            var hello=await Wire.Read(stream,alive.Token).WaitAsync(TimeSpan.FromSeconds(15));
            if(hello?.S("type")!="hello")throw new IOException("缺少设备握手");
            peer.Id=admin?"pc":hello.S("deviceId");peer.Name=admin?"本机":hello.S("name","Android 设备");
            if(peer.Id.Length<2||peer.Id.Length>100||peer.Id=="pc"&&!admin)throw new IOException("设备身份无效");
            string issuedKey="";
            if(!admin)
            {
                lock(Storage.Config)
                {
                    var known=Storage.Config.Devices.FirstOrDefault(x=>x.Id==peer.Id);
                    if(known is not null)
                    {
                        var provided=System.Text.Encoding.UTF8.GetBytes(hello.S("key"));var expected=System.Text.Encoding.UTF8.GetBytes(known.Key);
                        if(known.Revoked||provided.Length!=expected.Length||!CryptographicOperations.FixedTimeEquals(provided,expected))throw new IOException("设备授权已失效，请在 PC 重新配对");
                        known.Name=peer.Name;
                    }
                    else
                    {
                        if(lastFailure.TryGetValue(peer.Id,out var last)&&DateTime.UtcNow-last<TimeSpan.FromSeconds(3))throw new IOException("请稍后重试配对");
                        if(DateTime.UtcNow>pairExpires||hello.S("code")!=pairCode){lastFailure[peer.Id]=DateTime.UtcNow;throw new IOException("配对码错误或已过期");}
                        issuedKey=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                        Storage.Config.Devices.Add(new(){Id=peer.Id,Name=peer.Name,Key=issuedKey});Storage.Save();
                    }
                }
                // One live connection per mobile identity prevents duplicate input after switching transport.
                foreach(var old in peers.Where(x=>x.Value.Id==peer.Id&&!x.Value.Admin))old.Value.Dispose();
            }
            peers[connectionId]=peer;Changed();
            if(!admin)Storage.Audit(new{at=DateTimeOffset.UtcNow,eventType="connected",connectionId,device=peer.Id,transport});
            await Wire.Write(stream,new{type="welcome",protocol=1,key=issuedKey,host=Environment.MachineName,fingerprint=Storage.Fingerprint},peer.WriteLock,alive.Token);
            var push=Task.Run(async()=>
            {
                long sent=-1,lastGeneration=-1;string lastWatch="",lastOutput="";var heartbeat=DateTime.UtcNow;
                while(!alive.IsCancellationRequested)
                {
                    var now=Interlocked.Read(ref version);
                    if(now!=sent||DateTime.UtcNow-heartbeat>TimeSpan.FromSeconds(8))
                    {
                        var available=Sessions.Values.Where(s=>admin||s.Devices.Contains(peer.Id)).OrderBy(s=>s.Name).ToArray();
                        if(!available.Any(s=>s.Id==peer.Watch))peer.Watch=available.FirstOrDefault()?.Id??"";
                        var selected=available.FirstOrDefault(s=>s.Id==peer.Watch);
                        var generation=Interlocked.Read(ref peer.WatchGeneration);
                        var full=selected?.Output??"";var reset=lastGeneration!=generation||lastWatch!=peer.Watch||!full.StartsWith(lastOutput,StringComparison.Ordinal);
                        var delta=reset?full:full[lastOutput.Length..];
                        // Keep Bluetooth frames small and let logs catch up over several frames.
                        // Window snapshots must remain complete, including their first lines.
                        var limit=selected?.Kind=="observer"?120000:transport=="蓝牙"?8000:60000;
                        if(reset&&delta.Length>limit)delta=delta[^limit..];
                        else if(delta.Length>limit)delta=delta[..limit];
                        var consumed=reset?full: lastOutput+delta;
                        await Wire.Write(stream,new{type="state",version=now,host=Environment.MachineName,bluetooth=BluetoothStatus,lan=Storage.Config.LanEnabled,
                            sessions=available.Select(s=>s.Snapshot(false)).ToArray(),selected=peer.Watch,output=delta,reset,
                            commands=commandWindows.Select(w=>{var activity=windowActivities.GetOrAdd(w.Id,_=>new());lock(activity)return new{key=w.Id,name=w.WindowTitle,processId=w.ProcessId,windowHandle=w.WindowHandle,kind="observer",status=activity.Status,activityCycle=activity.Cycle,sessionId=Sessions.Values.FirstOrDefault(s=>s.Kind=="observer"&&s.ObservedWindowHandle==w.WindowHandle&&s.ObservedProcessId==w.ProcessId)?.Id??""};})
                              .Concat(Sessions.Values.Where(s=>s.Kind!="observer"&&s.Status!="disconnected"&&s.Status!="exited").Select(s=>new{key=s.Id,name=s.Name,processId=0,windowHandle=0L,kind=s.Kind,status=s.Status,activityCycle=0L,sessionId=s.Id})).ToArray(),
                            devices=admin?Storage.Config.Devices.Where(d=>!d.Revoked).Select(d=>new{d.Id,d.Name,online=peers.Values.Any(p=>p.Id==d.Id),transport=peers.Values.FirstOrDefault(p=>p.Id==d.Id)?.Transport??"离线"}).ToArray():null,
                            mobileInputs=admin?Sessions.Values.SelectMany(s=>s.PendingInputs()).ToArray():null,
                            pairing=admin?new{code=pairCode,expires=pairExpires,fingerprint=Storage.Fingerprint,port=Storage.Config.Port}:null},peer.WriteLock,alive.Token);
                        lastOutput=consumed;lastWatch=peer.Watch;lastGeneration=generation;sent=consumed==full?now:-1;heartbeat=DateTime.UtcNow;
                    }
                    await Task.Delay(transport=="蓝牙"?350:180,alive.Token);
                }
            },alive.Token);
            _=push.ContinueWith(task=>{if(task.IsFaulted){Storage.Audit(new{at=DateTimeOffset.UtcNow,eventType="pushFailed",device=peer.Id,error=task.Exception?.GetBaseException().Message});alive.Cancel();peer.Dispose();}},TaskScheduler.Default);
            while(await Wire.Read(stream,alive.Token) is { } msg)
            {
                if(msg.S("type")=="watch"){peer.Watch=msg.S("sessionId");Interlocked.Increment(ref peer.WatchGeneration);Changed();continue;}
                if(msg.S("type")=="ping"){await Wire.Write(stream,new{type="pong"},peer.WriteLock,alive.Token);continue;}
                var id=msg.S("id");if(id.Length<8||id.Length>100)throw new IOException("操作 ID 无效");
                object result;
                try
                {
                    var task=actions.GetOrAdd(peer.Id+":"+id,_=>new Lazy<Task<object>>(()=>ExecuteAudited(peer,msg)));result=await task.Value;
                    if(actions.Count>4096)foreach(var key in actions.Where(x=>x.Value.IsValueCreated&&x.Value.Value.IsCompleted).Take(1024).Select(x=>x.Key))actions.TryRemove(key,out _);
                }
                catch(Exception e){result=new{ok=false,error=e.Message};}
                await Wire.Write(stream,new{type="result",id,result},peer.WriteLock,alive.Token);
            }
            alive.Cancel();try{await push;}catch{}
        }
        catch(Exception e) when(e is IOException or OperationCanceledException or TimeoutException or ObjectDisposedException or SocketException)
        {Storage.Audit(new{at=DateTimeOffset.UtcNow,eventType="disconnect",transport=peer.Transport,device=peer.Id,reason=e.GetType().Name,message=e.Message});try{await Wire.Write(stream,new{type="error",error=e.Message},peer.WriteLock);}catch{}}
        finally{alive.Cancel();peers.TryRemove(connectionId,out _);if(!admin)Storage.Audit(new{at=DateTimeOffset.UtcNow,eventType="disconnected",connectionId,device=peer.Id,transport});Changed();}
    }
    private async Task<object> ExecuteAudited(Peer peer,JsonNode msg)
    {
        Storage.Audit(new{at=DateTimeOffset.UtcNow,id=msg.S("id"),device=peer.Id,action=msg.S("action"),session=msg.S("sessionId"),state="started"});
        var result=await Execute(peer,msg);
        Storage.Audit(new{at=DateTimeOffset.UtcNow,id=msg.S("id"),device=peer.Id,state="acknowledged"});return result;
    }
    private Session Find(Peer peer,string id,bool write)
    {
        if(!Sessions.TryGetValue(id,out var s)||(!peer.Admin&&!s.Devices.Contains(peer.Id)))throw new IOException("会话不存在或尚未映射到此设备");
        if(write&&!peer.Admin&&s.Controller!=peer.Id)throw new IOException("该命令行尚未映射为可输入，请在 PC 选择映射到移动端");return s;
    }
    private async Task<object> Execute(Peer peer,JsonNode msg)
    {
        var action=msg.S("action");
        if(action=="observeCatalog")
        {
            if(!peer.Admin)throw new IOException("仅桌面可报告窗口状态");
            var key=msg.S("key");if(!commandWindows.Any(w=>w.Id==key))return new{ok=true};
            var activity=windowActivities.GetOrAdd(key,_=>new());lock(activity){var revision=activity.Revision;activity.Observe(msg.S("text"));if(revision!=activity.Revision)Changed();}
            return new{ok=true};
        }
        if(action=="openCommand")
        {
            if(peer.Admin)throw new IOException("此操作用于已配对移动设备");
            Session target;
            var key=msg.S("key");
            if(Sessions.TryGetValue(key,out var managed)&&managed.Kind!="observer")target=managed;
            else
            {
                var window=CommandCatalog.Scan().FirstOrDefault(w=>w.Kind=="window"&&w.Id==key)??throw new IOException("命令行窗口已关闭，请刷新列表");
                await sessionWriter.WaitAsync();try
                {
                    target=Sessions.Values.FirstOrDefault(s=>s.Kind=="observer"&&s.ObservedWindowHandle==window.WindowHandle&&s.ObservedProcessId==window.ProcessId)!;
                    if(target is null){if(Sessions.Count>=10)throw new IOException("同时映射的命令行已达上限");target=new Session{Name=window.WindowTitle,Kind="observer",Cwd=window.Executable,Status="unknown",ObservedProcessId=window.ProcessId,ObservedWindowHandle=window.WindowHandle};Sessions[target.Id]=target;}
                }finally{sessionWriter.Release();}
            }
            lock(target.Gate){target.Devices.Add(peer.Id);target.Controller=peer.Id;}
            peer.Watch=target.Id;Interlocked.Increment(ref peer.WatchGeneration);SaveSessions();Changed();return new{ok=true,sessionId=target.Id};
        }
        if(action=="create")
        {
            if(!peer.Admin)throw new IOException("请从 PC 创建会话");
            await sessionWriter.WaitAsync();try
            {
                if(Sessions.Count>=5)throw new IOException("最多保留五个会话，请先移除已结束会话");
                var cwd=Path.GetFullPath(msg.S("cwd",Environment.CurrentDirectory));if(!Directory.Exists(cwd))throw new IOException("工作目录不存在");
                var s=new Session{Name=msg.S("name","新会话"),Kind=msg.S("kind","codex"),Cwd=cwd,FrontendPid=int.TryParse(msg.S("frontendPid"),out var pid)?pid:0};Sessions[s.Id]=s;
                try
                {
                    if(s.Kind=="codex"){await engine.Start(Storage.Config.CodexPath);var parameters=new JsonObject{["cwd"]=cwd};if(msg["config"] is JsonObject cfg)parameters["config"]=cfg.DeepClone();var result=await engine.Call("thread/start",parameters);s.ThreadId=result["thread"]!.S("id");s.Append("[Codex 会话已建立]\n");}
                    else if(s.Kind=="terminal")
                    {
                        var terminal=new TerminalProcess();s.Terminal=terminal;
                        terminal.Data+=text=>{s.Append(text);Changed();};terminal.Exited+=()=>{s.Status=terminal.ExitCode==0?"completed":"failed";s.Append("\n[终端进程已退出，退出码 "+terminal.ExitCode+"]\n");Changed();};
                        terminal.Start(msg.S("command","powershell.exe -NoLogo"),cwd);s.Status="running";
                    }
                    else throw new IOException("未知会话类型");
                }
                catch(Exception e){s.Status="failed";s.Append("[启动失败] "+e.Message+"\n");Changed();throw;}
                peer.Watch=s.Id;SaveSessions();Changed();return new{ok=true,sessionId=s.Id};
            }finally{sessionWriter.Release();}
        }
        if(action=="rotatePairing"){if(!peer.Admin)throw new IOException("仅 PC 可操作");RotateCode();return new{ok=true};}
        if(action=="observe")
        {
            if(!peer.Admin)throw new IOException("仅本机面板可以建立命令行观察");
            var pid=int.Parse(msg.S("processId"));var hwnd=long.Parse(msg.S("windowHandle"));
            if(!CommandCatalog.Scan().Any(p=>p.ProcessId==pid&&p.WindowHandle==hwnd&&p.Kind=="window"))throw new IOException("该终端窗口已关闭或无法识别");
            var existing=Sessions.Values.FirstOrDefault(s=>s.Kind=="observer"&&s.ObservedProcessId==pid&&s.ObservedWindowHandle==hwnd);
            if(existing is not null)return new{ok=true,sessionId=existing.Id};
            if(Sessions.Count>=10)throw new IOException("同时观察的会话已达上限");
            var observer=new Session{Name=msg.S("name","外部终端"),Kind="observer",Cwd=msg.S("cwd"),Status="unknown",ObservedProcessId=pid,ObservedWindowHandle=hwnd};
            Sessions[observer.Id]=observer;peer.Watch=observer.Id;Changed();return new{ok=true,sessionId=observer.Id};
        }
        if(action=="observeOutput")
        {
            if(!peer.Admin||!Sessions.TryGetValue(msg.S("sessionId"),out var observer)||observer.Kind!="observer")throw new IOException("映射终端不存在");
            if(int.Parse(msg.S("processId"))!=observer.ObservedProcessId||long.Parse(msg.S("windowHandle"))!=observer.ObservedWindowHandle)throw new IOException("终端窗口身份已改变");
            var text=msg.S("text");
            lock(observer.Gate){var old=observer.Status;if(observer.Output!=text){observer.ReplaceOutput(text);Changed();}observer.Activity.Observe(text);observer.Status=observer.Activity.Status;if(old!=observer.Status)Changed();}
            return new{ok=true};
        }
        if(action=="revoke")
        {
            if(!peer.Admin)throw new IOException("仅 PC 可操作");var deviceId=msg.S("deviceId");lock(Storage.Config){var d=Storage.Config.Devices.FirstOrDefault(d=>d.Id==deviceId);if(d!=null)d.Revoked=true;Storage.Save();}
            foreach(var p in peers.Values.Where(p=>p.Id==deviceId))p.Dispose();foreach(var s in Sessions.Values){s.Devices.Remove(deviceId);if(s.Controller==deviceId)s.Controller="pc";}SaveSessions();Changed();return new{ok=true};
        }
        if(action=="settings")
        {
            if(!peer.Admin)throw new IOException("仅 PC 可操作");Storage.Config.LanEnabled=msg.B("lan");Storage.Config.CodexPath=msg.S("codexPath",Storage.Config.CodexPath);Storage.Save();Changed();return new{ok=true,restartRequired=false};
        }
        var session=Find(peer,msg.S("sessionId"),action is "input" or "approve" or "interrupt" or "keys");
        if(action=="attach")
        {
            if(!peer.Admin)throw new IOException("仅本机终端可登记窗口");
            session.FrontendPid=int.Parse(msg.S("frontendPid"));Changed();return new{ok=true,kind=session.Kind};
        }
        if(action=="resize")
        {
            if(!peer.Admin)throw new IOException("终端尺寸由本机窗口决定");
            session.Terminal?.Resize((short)Math.Clamp(int.Parse(msg.S("cols")),20,300),(short)Math.Clamp(int.Parse(msg.S("rows")),5,150));return new{ok=true};
        }
        if(action=="map")
        {
            if(!peer.Admin)throw new IOException("仅 PC 可分配映射");var device=msg.S("deviceId");if(Storage.Config.Devices.All(d=>d.Id!=device||d.Revoked))throw new IOException("设备尚未配对");
            if(msg.B("enabled",true))session.Devices.Add(device);else session.Devices.Remove(device);
            session.Controller=msg.B("control")&&session.Devices.Contains(device)?device:"pc";
            if(msg.B("enabled",true))foreach(var remote in peers.Values.Where(p=>!p.Admin&&p.Id==device)){remote.Watch=session.Id;Interlocked.Increment(ref remote.WatchGeneration);}
            SaveSessions();Changed();return new{ok=true};
        }
        if(action=="takeControl"){if(!peer.Admin)throw new IOException("仅 PC 可收回控制权");session.Controller="pc";Changed();return new{ok=true};}
        if(action=="delete")
        {
            if(!peer.Admin)throw new IOException("仅 PC 可移除会话");if(session.Kind=="codex"&&session.Status is "running" or "approval" or "input")throw new IOException("请先中断正在运行的回合");
            session.Terminal?.Dispose();Sessions.TryRemove(session.Id,out _);SaveSessions();Changed();return new{ok=true};
        }
        if(action=="input")
        {
            var text=msg.S("text");if(text.Length==0||text.Length>64000)throw new IOException("输入长度应在 1–64000 字符之间");
            if(session.Kind=="observer")return await SendObserverInput(session,peer,text,msg.B("raw"));
            if(session.Kind=="terminal"){if(session.Status is "exited" or "completed" or "failed")throw new IOException("终端已经退出");await session.Terminal!.Write(text+(msg.B("raw")?"":"\r"));}
            else
            {
                lock(session.Gate){if(session.Status is "running" or "approval" or "input"){session.InputQueue.Enqueue(text);session.Append("\n[后续任务已排队]\n");Changed();return new{ok=true,queued=true};}}
                await StartTurn(session,text);
            }
            Changed();return new{ok=true};
        }
        if(action=="interrupt")
        {
            if(session.Kind=="observer")return await SendObserverInput(session,peer,"\u0003",true);
            if(session.Kind=="terminal")await session.Terminal!.Write("\u0003");
            else if(session.TurnId.Length>0)await engine.Call("turn/interrupt",new{threadId=session.ThreadId,turnId=session.TurnId});
            Changed();return new{ok=true};
        }
        if(action=="claimObserverInput")
        {
            if(!peer.Admin)throw new IOException("仅本机面板可投递终端按键");
            lock(session.Gate)
            {
                var input=session.ObserverInputs.FirstOrDefault(i=>i.Id==msg.S("requestId"));
                if(input is null||input.Claimed||input.Expires<=DateTime.UtcNow)throw new IOException("输入已处理或已失效");
                if(input.DeviceId!="pc"&&(!session.Devices.Contains(input.DeviceId)||session.Controller!=input.DeviceId))throw new IOException("设备映射已取消");
                input.Claimed=true;
            }
            return new{ok=true};
        }
        if(action=="observerInputResult")
        {
            if(!peer.Admin)throw new IOException("仅本机面板可确认终端输入");
            lock(session.Gate)session.ObserverInputs.FirstOrDefault(i=>i.Id==msg.S("requestId"))?.Completion.TrySetResult(msg.S("error"));
            return new{ok=true};
        }
        if(action=="approve")
        {
            Approval approval;lock(session.Gate){approval=session.Approvals.FirstOrDefault(a=>a.Id==msg.S("requestId"))??throw new IOException("此请求已处理或已失效");if(approval.Resolving)throw new IOException("此请求正在提交");approval.Resolving=true;}
            try
            {
                var decision=msg.S("decision","decline");JsonNode response;
                if(approval.Method=="item/tool/requestUserInput")response=msg["response"]?.DeepClone()??throw new IOException("缺少问题回答");
                else if(approval.Method=="mcpServer/elicitation/request")response=msg["response"]?.DeepClone()??J.Node(new{action="decline",content=(object?)null});
                else if(approval.Method=="item/permissions/requestApproval")response=decision=="accept"?J.Node(new{permissions=approval.Parameters["permissions"]?.DeepClone()??new JsonObject(),scope="turn"}):J.Node(new{permissions=new{}});
                else
                {
                    if(decision is not ("accept" or "decline" or "cancel"))throw new IOException("不支持此授权范围");
                    var allowed=approval.Parameters["availableDecisions"] as JsonArray;if(allowed is not null&&!allowed.Any(x=>x?.ToString()==decision))throw new IOException("上游未提供此选项");
                    response=J.Node(new{decision});
                }
                await engine.Send(new{id=approval.RawId.DeepClone(),result=response});
                lock(session.Gate){session.Approvals.Remove(approval);session.Status=session.Approvals.Count>0?"approval":"running";}
                session.Append("\n[用户交互已提交]\n");Changed();return new{ok=true};
            }
            catch{lock(session.Gate)approval.Resolving=false;Changed();throw;}
        }
        throw new IOException("未知操作");
    }
    private async Task<object> SendObserverInput(Session session,Peer peer,string text,bool raw)
    {
        if(text.Length>2048)throw new IOException("外部终端每次最多输入 2048 个字符");
        var input=new ObserverInput{Text=text,Raw=raw,DeviceId=peer.Id};
        lock(session.Gate){if(session.ObserverInputs.Count>=8)throw new IOException("终端正在处理前面的输入，请稍后");session.ObserverInputs.Add(input);}Changed();
        try
        {
            var error=await input.Completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if(error.Length>0)throw new IOException(error);
            return new{ok=true};
        }
        catch(TimeoutException){throw new IOException("桌面端未确认输入。为避免重复执行，此输入不会自动重发。");}
        finally{lock(session.Gate)session.ObserverInputs.Remove(input);Changed();}
    }
    private async Task StartTurn(Session s,string text)
    {
        if(s.Status=="disconnected")
        {await engine.Start(Storage.Config.CodexPath);await engine.Call("thread/resume",new{threadId=s.ThreadId});}
        s.Append("\n› "+text+"\n\n");s.Status="running";Changed();
        try{var result=await engine.Call("turn/start",new{threadId=s.ThreadId,input=new[]{new{type="text",text}}});s.TurnId=result["turn"]!.S("id");}
        catch(Exception e){s.Status="failed";s.Append("\n[请求失败] "+e.Message+"\n");Changed();throw;}
    }
    private void OnCodexMessage(JsonNode msg)
    {
        if(Environment.GetEnvironmentVariable("CODELINK_TRACE")=="1")Storage.Audit(new{at=DateTimeOffset.UtcNow,eventType="codex",method=msg.S("method"),requestId=msg["id"]?.DeepClone(),thread=msg["params"].S("threadId")});
        var p=msg["params"];if(p is null)return;var method=msg.S("method");var threadId=p.S("threadId",p["thread"].S("id"));
        var s=Sessions.Values.FirstOrDefault(x=>x.ThreadId==threadId);
        if(msg["id"] is not null&&(!InteractiveMethods.Contains(method)||s is null))
        {
            _=RejectUnsupportedRequest(msg["id"]!.DeepClone(),method);return;
        }
        if(s is null)return;
        lock(s.Gate)
        {
            if(msg["id"] is not null)
            {
                var approval=new Approval{Method=method,RawId=msg["id"]!.DeepClone(),Parameters=p.DeepClone()};s.Approvals.Add(approval);
                if(method.Contains("requestUserInput")){if(p.B("isBlocking"))s.Status="input";}else s.Status="approval";
                s.Append("\n["+approval.Summary+"]\n");
            }
            else switch(method)
            {
                case "turn/started":s.Status="running";s.TurnId=p["turn"].S("id");break;
                case "item/agentMessage/delta":s.Append(p.S("delta"));break;
                case "item/commandExecution/outputDelta":s.Append(p.S("delta"));break;
                case "item/started":if(p["item"].S("type")=="commandExecution")s.Append("\n$ "+p["item"].S("command")+"\n");break;
                case "item/completed":if(p["item"].S("type")=="agentMessage")s.Append("\n");break;
                case "serverRequest/resolved":s.Approvals.RemoveAll(a=>a.RawId.ToString()==p.S("requestId"));if(s.Approvals.Count==0&&s.Status is "approval" or "input")s.Status="running";break;
                case "turn/completed":
                    s.Status=p["turn"].S("status") switch{"completed"=>"completed","interrupted"=>"interrupted",_=>"failed"};
                    s.Approvals.Clear();if(p["turn"]?["error"] is not null)s.Append("\n[错误] "+p["turn"]!["error"]!.S("message")+"\n");
                    s.Append("\n[本轮"+(s.Status=="completed"?"完成":s.Status=="interrupted"?"已中断":"失败")+"]\n");
                    if(s.InputQueue.TryDequeue(out var next))_=Task.Run(async()=>{try{await StartTurn(s,next);}catch{}});SaveSessions();break;
            }
        }
        Changed();
    }
    private async Task RejectUnsupportedRequest(JsonNode id,string method)
    {
        try{await engine.Send(new{id,error=new{code=-32601,message="Code Link cannot route this request: "+method}});}catch(IOException){}
    }
    private void SaveSessions()
    {
        lock(Storage)File.WriteAllText(Path.Combine(Storage.DirectoryPath,"sessions.json"),J.Stringify(Sessions.Values.Where(s=>s.Kind=="codex").Select(s=>new{s.Id,s.Name,s.Kind,s.Cwd,s.ThreadId,s.Controller,devices=s.Devices.ToArray()})));
    }
    private void LoadSessions()
    {
        var file=Path.Combine(Storage.DirectoryPath,"sessions.json");if(!File.Exists(file))return;
        try{foreach(var n in JsonNode.Parse(File.ReadAllText(file))!.AsArray()){if(n is null||n.S("kind")!="codex")continue;var s=new Session{Id=n.S("id"),Name=n.S("name"),Kind="codex",Cwd=n.S("cwd"),ThreadId=n.S("threadId"),Controller="pc",Status="disconnected"};foreach(var d in n["devices"]!.AsArray())s.Devices.Add(d!.ToString());s.Append("[可恢复的历史会话；发送新任务时恢复，不重放之前的操作]\n");Sessions[s.Id]=s;}}catch{}
    }
    public void Dispose(){stop.Cancel();listener?.Stop();bluetooth?.Dispose();foreach(var p in peers.Values)p.Dispose();foreach(var s in Sessions.Values)s.Terminal?.Dispose();engine.Dispose();}
}
