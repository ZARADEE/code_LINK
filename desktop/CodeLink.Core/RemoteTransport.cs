using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CodeLink;

/// <summary>A dedicated loopback TLS ingress; the native library never sees the admin pipe.</summary>
public sealed class RemoteTransport : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly SemaphoreSlim clients=new(8);
    private readonly object gate=new();
    private ulong handle;
    private bool disposed;
    [DllImport("codelink_remote",CallingConvention=CallingConvention.Cdecl)]
    private static extern ulong cl_remote_start([MarshalAs(UnmanagedType.LPUTF8Str)]string config);
    [DllImport("codelink_remote",CallingConvention=CallingConvention.Cdecl)]
    private static extern nuint cl_remote_status(ulong handle,byte[] output,nuint capacity);
    [DllImport("codelink_remote",CallingConvention=CallingConvention.Cdecl)]
    private static extern void cl_remote_stop(ulong handle);

    public RemoteTransport(HostStorage storage,Func<TcpClient,CancellationToken,Task> accept)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Remote transport currently supports Windows.");
        lock(storage.Config)
        {
            if(string.IsNullOrEmpty(storage.Config.RemoteIdentity))
            {
                storage.Config.RemoteIdentity=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                storage.Save();
            }
        }
        try
        {
            listener.Start(8);
            handle=cl_remote_start(J.Stringify(new{role="server",secret=storage.Config.RemoteIdentity,targetPort=((IPEndPoint)listener.LocalEndpoint).Port,
                relayOnly=Environment.GetEnvironmentVariable("CODELINK_REMOTE_RELAY_ONLY")=="1"}));
            if(handle==0)throw new IOException("Remote transport configuration failed.");
            _=AcceptLoop(accept);
        }
        catch{listener.Stop();throw;}
    }
    private async Task AcceptLoop(Func<TcpClient,CancellationToken,Task> accept)
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var client=await listener.AcceptTcpClientAsync(stop.Token);
                if(!await clients.WaitAsync(0,stop.Token)){client.Dispose();continue;}
                _=Task.Run(async()=>{try{await accept(client,stop.Token);}finally{client.Dispose();clients.Release();}});
            }
        }
        catch(Exception error) when(error is OperationCanceledException or SocketException or ObjectDisposedException){}
    }
    public JsonNode Snapshot()
    {
        lock(gate)
        {
            if(disposed)return J.Node(new{state="stopped"});
            var bytes=new byte[65536];
            var needed=cl_remote_status(handle,bytes,(nuint)bytes.Length);
            if(needed==0||needed>(nuint)bytes.Length)return J.Node(new{state="error",error="Remote status unavailable."});
            return JsonNode.Parse(Encoding.UTF8.GetString(bytes,0,(int)needed-1))!;
        }
    }
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;stop.Cancel();listener.Stop();
            if(handle!=0){cl_remote_stop(handle);handle=0;}
        }
    }
}
