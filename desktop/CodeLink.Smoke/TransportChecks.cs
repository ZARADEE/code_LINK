using CodeLink;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Diagnostics;

namespace CodeLink.Smoke;

internal static class TransportChecks
{
    public static async Task Run()
    {
        var directory=Path.Combine(Environment.CurrentDirectory,".local","transport-check-"+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODELINK_INSTANCE","test-"+Guid.NewGuid().ToString("N"));
        var storage=new HostStorage(directory);
        var available=new TcpListener(IPAddress.Loopback,0);available.Start();var port=((IPEndPoint)available.LocalEndpoint).Port;available.Stop();
        storage.Config.Port=port;storage.Config.BluetoothEnabled=false;storage.Save();
        var start=new ProcessStartInfo(Path.Combine(Environment.CurrentDirectory,"desktop/CodeLink.Host/bin/Release/net10.0/CodeLink.Host.exe")){UseShellExecute=false,CreateNoWindow=true};
        start.Environment["CODELINK_DATA_DIR"]=directory;
        using var host=Process.Start(start)!;
        try {
        using var admin=new LocalClient();await admin.Connect(5000);
        await Wait(()=>admin.CurrentState is not null);
        var code=admin.CurrentState!["pairing"]!.S("code");var pin=storage.Fingerprint;
        using(var wrong=await Mobile.Connect(port,pin))
        {
            await wrong.Send(new{type="hello",deviceId="invalid-device",code="000000"});
            Require((await wrong.Next()).S("type")=="error","Invalid pairing code was accepted");
        }
        var wrongPinRejected=false;
        try{using var wrong=await Mobile.Connect(port,new string('0',64));}catch(System.Security.Authentication.AuthenticationException){wrongPinRejected=true;}
        Require(wrongPinRejected,"Invalid server certificate pin was accepted");
        using var mobile=await Mobile.Connect(port,pin);
        await mobile.Send(new{type="hello",deviceId="validation-pad",name="Transport checks",code});
        var welcome=await mobile.Next();Require(welcome.S("type")=="welcome","Pairing handshake failed");var key=welcome.S("key");
        var record=Path.Combine(directory,"received.txt").Replace("'","''");
        var shell="powershell.exe -NoLogo -NoProfile -Command \"while($true){$v=Read-Host 'INPUT_READY'; Add-Content -LiteralPath '"+record+"' -Value $v}\"";
        var created=await admin.Action("create",new{kind="terminal",name="Transport checks",cwd=directory,command=shell});var id=created.S("sessionId");
        await admin.Watch(id);await Wait(()=>admin.CurrentState?.S("output").Contains("INPUT_READY")==true);
        Require(!(await mobile.Action("input",new{sessionId=id,text="DENIED"})).B("ok"),"Unmapped mobile input was accepted");
        await admin.Action("map",new{sessionId=id,deviceId="validation-pad",enabled=true,control=true});
        const string operation="duplicate-operation-0001";
        Require((await mobile.Action("input",new{sessionId=id,text="ONCE"},operation)).B("ok"),"Mapped input failed");
        Require((await mobile.Action("input",new{sessionId=id,text="ONCE"},operation)).B("ok"),"Duplicate acknowledgment failed");
        await Wait(()=>File.Exists(record)&&File.ReadAllLines(record).Length>0);await Task.Delay(400);
        Require(File.ReadAllLines(record).SequenceEqual(new[]{"ONCE"}),"An operation ID executed more than once");
        await admin.Action("input",new{sessionId=id,text="PC_STILL_CONTROLS"});
        await Wait(()=>File.ReadAllLines(record).Length==2);
        using(var reconnect=await Mobile.Connect(port,pin))
        {
            await reconnect.Send(new{type="hello",deviceId="validation-pad",name="Transport checks",key});
            Require((await reconnect.Next()).S("type")=="welcome","Paired reconnect failed");
            var snapshot=await reconnect.Next();Require(snapshot.B("reset")&&snapshot["sessions"]!.AsArray().Any(s=>s.S("id")==id),"Reconnect did not restore mapping and snapshot");
            await admin.Action("map",new{sessionId=id,deviceId="validation-pad",enabled=false,control=false});
            Require(!(await reconnect.Action("input",new{sessionId=id,text="DENIED_AFTER_UNMAP"})).B("ok"),"Unmapped device retained input access");
        }
        await admin.Action("delete",new{sessionId=id});
        // Reconnection must deliver an entire window snapshot, even beyond the
        // 60 KB streaming chunk limit used by normal command output.
        var window=CommandCatalog.Scan().First(p=>p.Kind=="window");
        var observed=await admin.Action("observe",new{processId=window.ProcessId,windowHandle=window.WindowHandle,name="Snapshot transport check"});
        var observedId=observed.S("sessionId");
        var screen="SCREEN_START\n"+new string('W',80000)+"\nSCREEN_END";
        await admin.Action("observeOutput",new{sessionId=observedId,processId=window.ProcessId,windowHandle=window.WindowHandle,text=screen,viewport="VISIBLE_ROWS"});
        await admin.Action("map",new{sessionId=observedId,deviceId="validation-pad",enabled=true,control=true});
        using(var snapshotClient=await Mobile.Connect(port,pin))
        {
            await snapshotClient.Send(new{type="hello",deviceId="validation-pad",name="Transport checks",key});
            Require((await snapshotClient.Next()).S("type")=="welcome","Snapshot reconnect failed");
            var snapshot=await snapshotClient.Next();
            Require(snapshot.S("selected")==observedId&&snapshot.B("reset")&&snapshot.S("output")==screen,"The full external window snapshot was truncated");
            Require(snapshot.S("viewport")=="VISIBLE_ROWS","Desktop viewport must be separate from full history");
            await admin.Action("map",new{sessionId=observedId,deviceId="validation-pad",enabled=false,control=false});
            Require(!(await snapshotClient.Action("scroll",new{sessionId=observedId,lines=-3})).B("ok"),"Unmapped scrolling was accepted");
            await admin.Action("map",new{sessionId=observedId,deviceId="validation-pad",enabled=true,control=false});
            Require(!(await snapshotClient.Action("scroll",new{sessionId=observedId,lines=-3})).B("ok"),"Read-only device scrolled the desktop");
            await admin.Action("map",new{sessionId=observedId,deviceId="validation-pad",enabled=true,control=true});
            Require(!(await snapshotClient.Action("scroll",new{sessionId=observedId,lines=int.MinValue})).B("ok"),"Invalid scroll distance was accepted");
            var scroll=snapshotClient.Action("scroll",new{sessionId=observedId,lines=-3});
            await Wait(()=>admin.CurrentState?["mobileInputs"]?.AsArray().Any(i=>i.S("sessionId")==observedId&&i.S("operation")=="scroll")==true);
            var pending=admin.CurrentState!["mobileInputs"]!.AsArray().First(i=>i.S("sessionId")==observedId)!;
            Require(pending.S("text")==""&&pending.S("lines")=="-3","Scrolling must not synthesize terminal keys");
            // Revoke after enqueue, before the desktop claims the operation.
            await admin.Action("takeControl",new{sessionId=observedId});
            var denied=false;
            try{await admin.Action("claimObserverInput",new{sessionId=observedId,requestId=pending.S("id")});}catch(IOException){denied=true;}
            Require(denied,"Queued scroll retained permission after control was revoked");
            await admin.Action("observerInputResult",new{sessionId=observedId,requestId=pending.S("id"),error="Control revoked"});
            Require(!(await scroll).B("ok"),"Revoked scroll reported success");
        }
        await admin.Action("delete",new{sessionId=observedId});
        var config=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"host.json")))!;
        Require(config.S("CertificatePassword").StartsWith("dpapi:")&&config["Devices"]![0]!.S("Key").StartsWith("dpapi:"),"Pairing secrets were stored unprotected");
        using var cert=new HostStorage(directory).Certificate;
        Require(cert.Thumbprint==storage.Certificate.Thumbprint,"Encrypted credentials did not reload");
        var report=new{passed=true,invalidPairingRejected=true,wrongPinRejected,unmappedInputRejected=true,duplicateInputExecutedOnce=true,pcInputWhileMapped=true,reconnectRestoresMapping=true,unmapRevokesInput=true,credentialsProtectedAndReloadable=true,completeExternalSnapshot=true,separateDesktopViewport=true,scrollRequiresControl=true,queuedScrollRevocationChecked=true,invalidScrollRejected=true};
        File.WriteAllText("research/transport-acceptance.json",J.Stringify(report));Console.WriteLine(J.Stringify(report));
        } finally { if(!host.HasExited)host.Kill(true);storage.Certificate.Dispose(); }
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static async Task Wait(Func<bool> condition){for(var i=0;i<150;i++){try{if(condition())return;}catch(IOException){}await Task.Delay(100);}throw new TimeoutException("Expected state was not reached");}
    private sealed class Mobile:IDisposable
    {
        private readonly TcpClient tcp;private readonly SslStream ssl;private readonly SemaphoreSlim writer=new(1);
        private Mobile(TcpClient tcp,SslStream ssl){this.tcp=tcp;this.ssl=ssl;}
        public static async Task<Mobile> Connect(int port,string pin)
        {
            var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,port);
            var ssl=new SslStream(tcp.GetStream(),false,(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(pin,StringComparison.OrdinalIgnoreCase));
            try{await ssl.AuthenticateAsClientAsync("Code Link Local");return new(tcp,ssl);}catch{ssl.Dispose();tcp.Dispose();throw;}
        }
        public Task Send(object value)=>Wire.Write(ssl,value,writer);
        public async Task<JsonNode> Next()=>await Wire.Read(ssl).WaitAsync(TimeSpan.FromSeconds(15))??throw new IOException("Connection ended");
        public async Task<JsonNode> Action(string action,object fields,string? id=null)
        {
            id??=Guid.NewGuid().ToString("N");var msg=J.Node(fields);msg["type"]="action";msg["action"]=action;msg["id"]=id;await Send(msg);
            while(true){var answer=await Next();if(answer.S("type")=="result"&&answer.S("id")==id)return answer["result"]!;}
        }
        public void Dispose(){ssl.Dispose();tcp.Dispose();}
    }
}
