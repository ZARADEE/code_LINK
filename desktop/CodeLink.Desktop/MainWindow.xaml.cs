using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Threading;
using QRCoder;
using Forms=System.Windows.Forms;

namespace CodeLink.Desktop;
public partial class MainWindow:Window
{
    private readonly LocalClient client=new();
    private JsonNode? state;
    private bool rendering,quitting,scanning;
    private string selected="",deviceSignature="";
    private string? externalSelection;
    private readonly Dictionary<string,string> outputCache=new(),statuses=new();
    private readonly HashSet<string> dispatchedInputs=new();
    private readonly SemaphoreSlim externalInputGate=new(1);
    private bool observing;
    private readonly Dictionary<string,DateTime> observationSent=new();
    private readonly Dictionary<string,string> observedViewports=new();
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer scanTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private readonly DispatcherTimer usbTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private readonly DispatcherTimer observeTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool checkingUsb;
    private readonly SemaphoreSlim usbGate=new(1);
    private string adbPort="5038";
    private readonly HashSet<string> knownUsb=new();
    private readonly ObservableCollection<ExternalItem> externalItems=new();
    private record Item(string Id,string Label){public override string ToString()=>Label;}
    private sealed class ExternalItem(CommandCandidate process):INotifyPropertyChanged
    {
        public CommandCandidate Process{get;private set;}=process;
        public string? SessionId{get;set;}
        public string Key=>SessionId??Process.Id;
        public string Label=>SessionId is null?Process.Label:Process.WindowTitle;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Snapshot{get;private set;}="";
        public void Update(CommandCandidate next){if(Process==next)return;Process=next;PropertyChanged?.Invoke(this,new(nameof(Label)));}
        public void UpdateSnapshot(string next){Snapshot=next;}
        public override string ToString()=>Label;
    }

    public MainWindow()
    {
        InitializeComponent();Icon=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"assets/icon.png")));
        ExternalProcessList.ItemsSource=externalItems;
        tray=new Forms.NotifyIcon{Icon=new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"assets/app.ico")),Visible=true,Text="Code Link"};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestorePanel);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("打开面板",null,(_,_)=>Dispatcher.Invoke(RestorePanel));
        menu.Items.Add("退出面板，保留命令行",null,(_,_)=>Dispatcher.Invoke(()=>{quitting=true;Close();Application.Current.Shutdown();}));
        tray.ContextMenuStrip=menu;
        Closing+=(_,e)=>{if(!quitting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>{scanTimer.Stop();usbTimer.Stop();observeTimer.Stop();tray.Dispose();client.Dispose();};
        scanTimer.Tick+=async(_,_)=>await Scan();
        usbTimer.Tick+=async(_,_)=>await RefreshUsbMappings();
        observeTimer.Tick+=async(_,_)=>await RefreshSelectedExternalOutput();
        Loaded+=async(_,_)=>{AnimateSplash();await Connect();await Scan();scanTimer.Start();usbTimer.Start();observeTimer.Start();};
        DeviceList.SelectionChanged+=(_,_)=>{if(!rendering)UpdateMappingButton();};
    }

    public void RestorePanel(){Show();WindowState=WindowState.Normal;Activate();}

    private async void AnimateSplash()
    {
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
        try
        {
            var gif=new GifBitmapDecoder(new Uri(Path.Combine(AppContext.BaseDirectory,"assets/startup.gif")),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var frame=0;timer.Tick+=(_,_)=>SplashImage.Source=gif.Frames[frame++%gif.Frames.Count];SplashImage.Source=gif.Frames[0];timer.Start();await Task.Delay(1800);
        }
        catch(IOException){}
        finally{timer.Stop();Splash.Visibility=Visibility.Collapsed;}
    }
    private async Task Connect()
    {
        client.State+=s=>Dispatcher.InvokeAsync(()=>Render(s));
        client.Disconnected+=()=>Dispatcher.InvokeAsync(()=>{HostStatus.Text="代理连接中断";MapMobileButton.IsEnabled=false;});
        try{await HostLauncher.Connect(client);}
        catch(Exception e){HostStatus.Text="代理未启动";Footer.Text=e.Message;}
    }
    private static string Strip(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))","").Replace("\r","");
    private static string Status(string value)=>value switch{"running"=>"运行中","approval"=>"等待授权","input"=>"等待回答","completed"=>"本轮完成","failed"=>"失败","interrupted"=>"已中断","exited"=>"进程退出","disconnected"=>"等待恢复",_=>"就绪"};
    private JsonNode? Current=>state?["sessions"]?.AsArray().FirstOrDefault(s=>s.S("id")==selected);
    private void Render(JsonNode next)
    {
        state=next;
        if(next["mobileInputs"] is JsonArray pendingInputs)
            foreach(var input in pendingInputs)if(input is not null&&dispatchedInputs.Add(input.S("id")))_=DispatchExternalInput(input.DeepClone());
        var streamId=next.S("selected");outputCache.TryGetValue(streamId,out var previous);
        var value=(next.B("reset")?"":previous??"")+next.S("output");
        outputCache[streamId]=value.Length>120000?value[^100000..]:value;
        var sessions=next["sessions"]!.AsArray();var devices=next["devices"]!.AsArray();
        rendering=true;
        if(!sessions.Any(s=>s.S("id")==selected))selected=streamId;
        var oldDevice=(DeviceList.SelectedItem as Item)?.Id;
        var ds=devices.ToJsonString();
        if(ds!=deviceSignature)
        {
            deviceSignature=ds;DeviceList.ItemsSource=devices.Select(d=>new Item(d!.S("id"),(d.B("online")?"● ":"○ ")+d.S("name")+"\n"+UiMessages.Server(d.S("transport")))).ToArray();
            DeviceList.SelectedItem=DeviceList.Items.Cast<Item>().FirstOrDefault(d=>d.Id==oldDevice)??DeviceList.Items.Cast<Item>().FirstOrDefault();
        }
        HostStatus.Text="代理在线 · 蓝牙 "+UiMessages.Server(next.S("bluetooth"));
        if(externalSelection is null)RenderOutput();
        foreach(var s in sessions)
        {
            var id=s!.S("id");var status=s.S("status");
            if(statuses.TryGetValue(id,out var old)&&old!=status&&status is "approval" or "input" or "completed" or "failed")tray.ShowBalloonTip(4000,s.S("name"),Status(status),Forms.ToolTipIcon.Info);
            statuses[id]=status;
        }
        Footer.Text=$"{devices.Count} 台移动设备 · {sessions.Count} 个受控命令行";
        rendering=false;UpdateMappingButton();
    }
    private async Task DispatchExternalInput(JsonNode input)
    {
        await externalInputGate.WaitAsync();
        try
        {
            try{await client.Action("claimObserverInput",new{sessionId=input.S("sessionId"),requestId=input.S("id")});}
            catch{return;}
            var error="";
            try
            {
                var pid=int.Parse(input.S("processId"));var handle=long.Parse(input.S("windowHandle"));
                if(input.S("operation")=="scroll"&&input.B("latest"))
                {
                    var target=await Task.Run(()=>WindowPointer.BackToBottom(pid,handle));
                    if(target is not null)foreach(var field in target)input[field.Key]=field.Value?.DeepClone();
                }
                if(input.S("operation")=="scroll")
                {
                    await Task.Run(()=>WindowScroll.Apply(int.Parse(input.S("processId")),long.Parse(input.S("windowHandle")),int.Parse(input.S("lines","0")),input.B("latest")));
                    await RefreshSelectedExternalOutput();
                }
                else
                {
                if(input.S("operation")=="pointer")await Task.Run(()=>WindowPointer.Validate(pid,handle,int.Parse(input.S("row")),int.Parse(input.S("column")),input.S("expectedLine")));
                var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"CodeLink.ConsoleBridge.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
                using var process=Process.Start(start)??throw new IOException("无法启动终端输入连接器");
                await process.StandardInput.WriteAsync(input.ToJsonString());process.StandardInput.Close();
                var output=process.StandardOutput.ReadToEndAsync();
                try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}catch{process.Kill();throw;}
                var result=JsonNode.Parse(await output);if(!result.B("ok"))throw new IOException(result.S("error","终端未确认输入"));
                if(input.S("operation")=="pointer")await RefreshSelectedExternalOutput();
                }
            }
            catch(Exception e){error=e.Message;}
            await Run(()=>client.Action("observerInputResult",new{sessionId=input.S("sessionId"),requestId=input.S("id"),error}));
            if(error.Length>0)Footer.Text=error;
        }
        finally{externalInputGate.Release();}
    }
    private void RenderOutput()
    {
        var current=Current;
        SessionTitle.Text=current?.S("name")??"选择命令行";
        SessionSubtitle.Text=current is null?"从终端运行 clink terminal 或 clink codex":Status(current.S("status"))+" · "+current.S("cwd");
        var content=outputCache.TryGetValue(selected,out var output)?Strip(output):"";
        if(Output.Text!=content){var follow=Output.VerticalOffset>=Output.ExtentHeight-Output.ViewportHeight-30;Output.Text=content;if(follow)Output.ScrollToEnd();}
        PendingStatus.Text=current?["approvals"] is JsonArray requests&&requests.Count>0?$"{requests.Count} 项交互待处理，请在命令行或移动端处理。":"";
    }
    private void UpdateMappingButton()
    {
        var device=DeviceList.SelectedItem as Item;var session=Current;
        var deviceOnline=state?["devices"]?.AsArray().Any(d=>d.S("id")==device?.Id&&d.B("online"))==true;
        var external=externalItems.FirstOrDefault(item=>item.Process.Id==externalSelection);
        if(external is not null)
        {
            var observer=state?["sessions"]?.AsArray().FirstOrDefault(s=>s.S("kind")=="observer"&&s.S("observedProcessId")==external.Process.ProcessId.ToString()&&s.S("observedWindowHandle")==external.Process.WindowHandle.ToString());
            var mapped=observer?["devices"] is JsonArray observed&&observed.Any(id=>id?.ToString()==device?.Id);
            MapMobileButton.IsEnabled=external.Process.Kind=="window"&&external.Process.WindowHandle!=0&&deviceOnline;
            MapMobileButton.Content=!deviceOnline?"平板未连接":mapped?"取消映射":"映射到移动端";return;
        }
        var mappedSession=session?["devices"] is JsonArray ids&&ids.Any(id=>id?.ToString()==device?.Id);
        MapMobileButton.IsEnabled=deviceOnline&&session is not null;
        MapMobileButton.Content=mappedSession?"取消映射":"映射到移动端";
    }
    private async void SelectExternalProcess(object sender,SelectionChangedEventArgs e)
    {
        if(rendering||ExternalProcessList.SelectedItem is not ExternalItem item)return;
        if(item.SessionId is {} sessionId){selected=sessionId;externalSelection=null;RenderOutput();UpdateMappingButton();await Run(()=>client.Watch(selected));return;}
        externalSelection=item.Process.Id;
        RenderExternal(item);
        UpdateMappingButton();
    }
    private void RenderExternal(ExternalItem item)
    {
        SessionTitle.Text=string.IsNullOrWhiteSpace(item.Process.WindowTitle)?item.Process.DisplayName:item.Process.WindowTitle;
        SessionSubtitle.Text=$"PID {item.Process.ProcessId} · "+(item.Process.Executable.Length>0?item.Process.Executable:"进程路径不可读取");
        var content=item.Snapshot.Length==0?"正在读取终端窗口…\n\n窗口标题：\n"+item.Process.WindowTitle:item.Snapshot;
        if(Output.Text!=content)Output.Text=content;
        PendingStatus.Text=item.Process.Kind=="window"?"移动端可输入文字、按键并响应终端内的授权提示。映射跟随该窗口当前活动的终端标签页。":"请选择该进程所在的终端窗口进行映射。";
    }
    private async Task Scan()
    {
        if(scanning)return;scanning=true;
        try
        {
            var excluded=state?["sessions"]?.AsArray().SelectMany(s=>new[]{s.S("processId"),s.S("frontendPid")}).Select(value=>int.TryParse(value,out var id)?id:0).Where(id=>id>0).ToHashSet()??new HashSet<int>();
            var processes=await Task.Run(()=>CommandCatalog.Scan(excluded));
            var entries=processes.Where(p=>p.Kind=="window").Select(p=>new ExternalItem(p)).ToList();
            var managed=state?["sessions"]?.AsArray().Where(s=>s.S("kind")!="observer"&&s.S("status")!="disconnected"&&s.S("status")!="exited").ToArray()??[];
            foreach(var session in managed)entries.Add(new ExternalItem(new CommandCandidate(0,session.S("kind"),"",session.S("name")+"\n"+Status(session.S("status")),Kind:"managed")){SessionId=session.S("id")});
            if(entries.Count==0)entries.AddRange(processes.Select(p=>new ExternalItem(p)));
            rendering=true;
            var ids=entries.Select(p=>p.Key).ToHashSet();
            for(var i=externalItems.Count-1;i>=0;i--)if(!ids.Contains(externalItems[i].Key))externalItems.RemoveAt(i);
            for(var i=0;i<entries.Count;i++)
            {
                var entry=entries[i];var existing=externalItems.FirstOrDefault(item=>item.Key==entry.Key);
                if(existing is null)externalItems.Insert(i,entry);
                else{existing.Update(entry.Process);var index=externalItems.IndexOf(existing);if(index!=i)externalItems.Move(index,i);}
            }
            ExternalProcessList.SelectedItem=externalItems.FirstOrDefault(p=>externalSelection is null?p.SessionId==selected:p.Process.Id==externalSelection);
            if(externalSelection is not null&&ExternalProcessList.SelectedItem is null){externalSelection=null;RenderOutput();}
            else if(ExternalProcessList.SelectedItem is ExternalItem item){if(item.SessionId is null)RenderExternal(item);else RenderOutput();}
            rendering=false;UpdateMappingButton();
        }
        catch(Exception e){Footer.Text="扫描失败："+e.Message;}
        finally{rendering=false;scanning=false;}
    }
    private static string ReadTerminalWindow(long handle,bool visibleOnly=false)
    {
        try
        {
            var window=AutomationElement.FromHandle(new IntPtr(handle));
            if(window is null)return "[终端窗口已关闭]";
            var found=window.FindAll(TreeScope.Descendants,System.Windows.Automation.Condition.TrueCondition);
            var best="";
            for(var i=0;i<found.Count;i++)
                if(found[i].TryGetCurrentPattern(TextPattern.Pattern,out var pattern)&&pattern is TextPattern text)
                {
                    var value=(visibleOnly?string.Join("",text.GetVisibleRanges().Select(range=>range.GetText(-1))):text.DocumentRange.GetText(-1)).TrimEnd('\0');
                    if(value.Length>120000)value=value[^120000..];
                    if(value.Length>best.Length)best=value;
                }
            return best.Length==0?"[此终端未公开可读取的文本缓冲]":best;
        }
        catch(ElementNotAvailableException){return "[终端窗口已关闭]";}
        catch(InvalidOperationException){return "[终端文本暂不可读取]";}
    }
    private async Task RefreshSelectedExternalOutput()
    {
        if(observing||state is null)return;observing=true;
        try
        {
            var targets=state["sessions"]!.AsArray().Where(s=>s is not null&&s.S("kind")=="observer"&&s["devices"]!.AsArray().Count>0)
                .Select(s=>(Id:s.S("id"),Pid:int.Parse(s.S("observedProcessId")),Handle:long.Parse(s.S("observedWindowHandle")))).ToList();
            var chosen=externalItems.FirstOrDefault(item=>item.Process.Id==externalSelection);
            foreach(var item in externalItems.Where(i=>i.Process.Kind=="window"))
                if(!targets.Any(t=>t.Handle==item.Process.WindowHandle))targets.Add(("",item.Process.ProcessId,item.Process.WindowHandle));
            if(IsVisible&&chosen is not null&&chosen.Process.WindowHandle!=0&&!targets.Any(t=>t.Handle==chosen.Process.WindowHandle))targets.Add(("",chosen.Process.ProcessId,chosen.Process.WindowHandle));
            foreach(var target in targets)
            {
                var snapshot=await Task.Run(()=>ReadTerminalWindow(target.Handle));
                var item=externalItems.FirstOrDefault(candidate=>candidate.Process.WindowHandle==target.Handle&&candidate.Process.ProcessId==target.Pid);
                var changed=item is null||snapshot!=item.Snapshot;
                item?.UpdateSnapshot(snapshot);
                if(IsVisible&&item is not null&&item.Process.Id==externalSelection)RenderExternal(item);
                var catalogKey=target.Pid+":"+target.Handle;
                if(changed||!observationSent.TryGetValue(catalogKey,out var last)||DateTime.UtcNow-last>TimeSpan.FromSeconds(2))
                {await Run(()=>client.Action("observeCatalog",new{key=catalogKey,text=snapshot}));observationSent[catalogKey]=DateTime.UtcNow;}
                if(target.Id.Length>0)
                {
                    var viewport=await Task.Run(()=>ReadTerminalWindow(target.Handle,true));
                    if(changed||!observedViewports.TryGetValue(target.Id,out var previousViewport)||previousViewport!=viewport||!observationSent.TryGetValue(target.Id,out var sent)||DateTime.UtcNow-sent>TimeSpan.FromSeconds(2))
                    {await Run(()=>client.Action("observeOutput",new{sessionId=target.Id,processId=target.Pid,windowHandle=target.Handle,text=snapshot,viewport}));observationSent[target.Id]=DateTime.UtcNow;observedViewports[target.Id]=viewport;}
                }
            }
        }
        catch(Exception e){Footer.Text="读取终端失败："+e.Message;}
        finally{observing=false;}
    }
    private async void ScanProcesses(object sender,RoutedEventArgs e)=>await Scan();
    private async void MapControl(object sender,RoutedEventArgs e)
    {
        if(DeviceList.SelectedItem is not Item device)return;
        if(externalSelection is not null)
        {
            var external=externalItems.FirstOrDefault(item=>item.Process.Id==externalSelection);if(external is null)return;
            var observer=state?["sessions"]?.AsArray().FirstOrDefault(s=>s.S("kind")=="observer"&&s.S("observedProcessId")==external.Process.ProcessId.ToString()&&s.S("observedWindowHandle")==external.Process.WindowHandle.ToString());
            if(observer is null)
            {
                try
                {
                    var result=await client.Action("observe",new{processId=external.Process.ProcessId,windowHandle=external.Process.WindowHandle,name=external.Process.WindowTitle,cwd=external.Process.Executable});
                    var id=result.S("sessionId");await client.Action("map",new{sessionId=id,deviceId=device.Id,enabled=true,control=true});
                    await client.Watch(id);await Task.Delay(200);
                    var snapshot=external.Snapshot.Length==0?await Task.Run(()=>ReadTerminalWindow(external.Process.WindowHandle)):external.Snapshot;
                    external.UpdateSnapshot(snapshot);
                    await client.Action("observeOutput",new{sessionId=id,processId=external.Process.ProcessId,windowHandle=external.Process.WindowHandle,text=snapshot});RenderExternal(external);
                }
                catch(Exception error){Footer.Text=error.Message;}
            }
            else
            {
                var mapped=observer["devices"] is JsonArray devices&&devices.Any(id=>id?.ToString()==device.Id);
                await Run(()=>client.Action("map",new{sessionId=observer.S("id"),deviceId=device.Id,enabled=!mapped,control=!mapped}));
            }
            return;
        }
        if(Current is not {} current)return;
        var enabled=!(current["devices"] is JsonArray ids&&ids.Any(id=>id?.ToString()==device.Id));
        await Run(()=>client.Action("map",new{sessionId=selected,deviceId=device.Id,enabled,control=enabled}));
    }
    private async Task Run(Func<Task> action){try{await action();}catch(Exception e){Footer.Text=e.Message;}}

    private string Invite(string address,string attempt="",string transport="")
    {
        var p=state?["pairing"]??throw new IOException("代理尚未就绪");
        if(transport=="Remote")
        {
            var remote=state?["remote"];
            if(!remote.B("enabled")||!remote.B("relayOnline"))throw new IOException("远程入口尚未就绪，请稍候。");
            return "codelink:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(J.Stringify(new{host="127.0.0.1",port=int.Parse(p.S("port")),pin=p.S("fingerprint"),code=p.S("remoteCode"),attempt,transport,remoteProtocol=1,endpoint=remote.S("endpoint"),relays=remote?["relays"]?.DeepClone()}))).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        return "codelink:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(J.Stringify(new{host=address,port=int.Parse(p.S("port")),pin=p.S("fingerprint"),code=p.S("code"),attempt,transport}))).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    private async void PairDevice(object sender,RoutedEventArgs e)
    {
        await Run(async()=>
        {
            if(state is null)throw new IOException("代理尚未就绪");
            await RefreshPairing(CancellationToken.None);
            new PairDeviceWindow(this,(address,transport)=>Invite(address,transport:transport),
                async enabled=>{await client.Action("settings",new{lan=enabled||state.B("lan")});},
                (progress,cancel)=>UsbConnect(progress,cancel),
                ()=>state?["devices"]?.AsArray().Count(d=>d.B("online"))??0,
                ()=>UiMessages.Server(state.S("bluetooth")),
                ()=>state?["devices"]?.AsArray().Where(d=>d.B("online")&&d.S("transport")=="Bluetooth").Select(d=>d.S("name")).ToArray()??Array.Empty<string>(),
                ()=>RefreshPairing(CancellationToken.None),
                async enabled=>{var result=await client.Action("remoteAccess",new{enabled});if(!result.B("ok"))throw new IOException(result.S("error"));},
                ()=>state?["remote"]).ShowDialog();
        });
    }
    private string AdbPath=>new[]{Path.Combine(AppContext.BaseDirectory,"tools/adb.exe"),Path.Combine(Environment.CurrentDirectory,".tools/platform-tools/adb.exe")}.FirstOrDefault(File.Exists)??throw new IOException("发布目录缺少 ADB");
    private Task<string> Adb(params string[] args)=>Adb(CancellationToken.None,args);
    private async Task<string> Adb(CancellationToken cancel,params string[] args)
    {
        var start=new ProcessStartInfo(AdbPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in new[]{"-P",adbPort}.Concat(args))start.ArgumentList.Add(a);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromSeconds(10),cancel);}catch{if(!process.HasExited)process.Kill();throw;}
        if(process.ExitCode!=0)throw new IOException(await error);return await output;
    }
    private async Task<string[]> UsbDevices()=> (await Adb("devices","-l")).Split('\n').Where(s=>System.Text.RegularExpressions.Regex.IsMatch(s,@"^\S+\s+device\b")&&s.Contains("usb:",StringComparison.OrdinalIgnoreCase)).Select(s=>s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[0]).ToArray();
    private async Task<string> UsbSerial(CancellationToken cancel=default)
    {
        var errors=new List<string>();
        foreach(var port in new[]{adbPort,adbPort=="5038"?"5037":"5038"})
        {
            adbPort=port;
            try
            {
                var deviceState=(await Adb(cancel,"-d","get-state")).Trim();
                if(deviceState!="device"){errors.Add(deviceState);continue;}
                var serial=(await Adb(cancel,"-d","get-serialno")).Trim();if(serial.Length>0&&serial!="unknown")return serial;
            }catch(IOException e){errors.Add(e.Message);}
        }
        var detail=string.Join(" ",errors);
        if(detail.Contains("unauthorized",StringComparison.OrdinalIgnoreCase))throw new IOException("平板尚未授权。解锁 Android，在 USB 调试弹窗中允许这台电脑，然后重试。");
        if(detail.Contains("offline",StringComparison.OrdinalIgnoreCase))throw new IOException("USB 设备离线。重新插拔数据线，等待平板被系统识别后重试。");
        if(detail.Contains("more than one",StringComparison.OrdinalIgnoreCase))throw new IOException("检测到多台 USB 设备。首次配对先只连接一台平板，然后重试。");
        throw new IOException("未发现已授权的 USB 设备。请连接支持数据传输的线缆，开启 USB 调试并解锁平板；必要时更换 USB 接口。");
    }
    private async Task RefreshUsbMappings()
    {
        if(checkingUsb||state is null||!await usbGate.WaitAsync(0))return;checkingUsb=true;
        try
        {
            // ADB's Windows backend can omit usb:; -d selects a physical USB transport.
            var serial=await UsbSerial();
            if(serial.Length>0&&serial!="unknown")
            {
                var port=state["pairing"]!.S("port");
                var listing=await Adb("-s",serial,"reverse","--list");
                if(!listing.Contains("tcp:"+port))await Adb("-s",serial,"reverse","tcp:"+port,"tcp:"+port);
                knownUsb.Add(serial);
            }
        }catch(IOException){}catch(TimeoutException){}finally{checkingUsb=false;usbGate.Release();}
    }
    private async Task RefreshPairing(CancellationToken cancel)
    {
        var old=state?["pairing"].S("code");
        await client.Action("rotatePairing");
        for(var i=0;i<30;i++)
        {
            cancel.ThrowIfCancellationRequested();
            if(state?["pairing"].S("code") is {Length:>0} current&&current!=old)return;
            await Task.Delay(100,cancel);
        }
        throw new IOException("配对信息尚未刷新。请关闭连接窗口后重试。");
    }
    private async Task<string> UsbConnect(Action<string> progress,CancellationToken cancel)
    {
        await usbGate.WaitAsync(cancel);
        try
        {
        progress("1 / 3 · 正在检测 USB 调试授权…");
        var serial=await UsbSerial(cancel);
        var port=state!["pairing"]!.S("port");
        progress("2 / 3 · 设备已找到，正在建立 USB 通道…");
        await Adb(cancel,"-s",serial,"reverse","tcp:"+port,"tcp:"+port);
        var installed=await Adb(cancel,"-s",serial,"shell","pm","list","packages","com.codelink.app");
        if(!installed.Split('\n').Any(line=>line.Trim()=="package:com.codelink.app"))throw new IOException("平板尚未安装 Code Link。请先安装 Release 中的 Android APK，再点击 USB 一键连接。");
        await RefreshPairing(cancel);
        var attempt=Guid.NewGuid().ToString("N");
        var invite=Invite("127.0.0.1",attempt);
        var started=await Adb(cancel,"-s",serial,"shell","am","start","-n","com.codelink.app/.MainActivity","--es","invite",invite,"--es","mode","USB");
        if(started.Contains("Error"))throw new IOException(started);
        progress("3 / 3 · 应用已打开，正在等待 Android 确认连接…");
        for(var i=0;i<60;i++)
        {
            cancel.ThrowIfCancellationRequested();
            var device=state?["devices"]?.AsArray().FirstOrDefault(d=>d.B("online")&&d.S("pairingAttempt")==attempt);
            if(device is not null)return "USB 已连接 · "+device.S("name")+"\n下一步：在手机或平板选择一个测试终端，发送 echo hello，并确认输出中出现 hello。";
            await Task.Delay(250,cancel);
        }
        throw new IOException("Android 尚未确认连接。请解锁设备，检查应用里的连接提示，并确认电脑和 Android 使用同一版本，然后重试。");
        }
        finally{usbGate.Release();}
    }
}
