using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodeLink;
// Native Winsock RFCOMM: no serial COM-port setup or third-party Bluetooth driver.
public sealed class BluetoothServer : IDisposable
{
    public static readonly Guid ServiceId=new("944d82d8-3d07-4c96-9d86-04a95bf20579");
    private IntPtr socketHandle=new(-1);
    private IntPtr guidPtr,addressPtr,csPtr,namePtr;
    private QuerySet registration;
    private bool registered;
    public void Start()
    {
        if(WSAStartup(0x0202,new byte[512])!=0)throw new IOException("蓝牙套接字初始化失败");
        socketHandle=socket(32,1,3);Check(socketHandle!=new IntPtr(-1));
        var enabled=1;
        Check(setsockopt(socketHandle,3,unchecked((int)0x80000001),ref enabled,4)==0);
        Check(setsockopt(socketHandle,3,2,ref enabled,4)==0);
        var address=new BthAddress{Family=32,Port=0xffffffff};
        Check(bind(socketHandle,ref address,Marshal.SizeOf<BthAddress>())==0);
        var size=Marshal.SizeOf<BthAddress>();Check(getsockname(socketHandle,ref address,ref size)==0);Check(listen(socketHandle,4)==0);
        guidPtr=Marshal.AllocHGlobal(16);Marshal.StructureToPtr(ServiceId,guidPtr,false);
        addressPtr=Marshal.AllocHGlobal(size);Marshal.StructureToPtr(address,addressPtr,false);
        var cs=new CsAddress{Local=new(){Address=addressPtr,Length=size},SocketType=1,Protocol=3};
        csPtr=Marshal.AllocHGlobal(Marshal.SizeOf<CsAddress>());Marshal.StructureToPtr(cs,csPtr,false);
        namePtr=Marshal.StringToHGlobalUni("Code Link");
        registration=new QuerySet{Size=Marshal.SizeOf<QuerySet>(),ServiceInstanceName=namePtr,ServiceClassId=guidPtr,Namespace=16,NumberOfCsAddrs=1,CsBuffer=csPtr};
        Check(WSASetService(ref registration,0,0)==0);registered=true;
    }
    public Stream Accept(){var peer=accept(socketHandle,IntPtr.Zero,IntPtr.Zero);Check(peer!=new IntPtr(-1));return new BthStream(peer);}
    private static void Check(bool success){if(!success)throw new IOException("蓝牙服务不可用（"+WSAGetLastError()+"）");}
    public void Dispose(){if(registered){WSASetService(ref registration,2,0);registered=false;}if(socketHandle!=new IntPtr(-1)){closesocket(socketHandle);socketHandle=new(-1);}foreach(var p in new[]{guidPtr,addressPtr,csPtr,namePtr})if(p!=IntPtr.Zero)Marshal.FreeHGlobal(p);guidPtr=addressPtr=csPtr=namePtr=IntPtr.Zero;}
    private sealed class BthStream(IntPtr handle):Stream
    {
        private IntPtr s=handle;
        public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count){var bytes=offset==0?buffer:new byte[count];var n=recv(s,bytes,count,0);if(n<0)throw new IOException("蓝牙连接已断开");if(offset!=0&&n>0)Buffer.BlockCopy(bytes,0,buffer,offset,n);return n;}
        public override void Write(byte[] buffer,int offset,int count){var sent=0;while(sent<count){var bytes=buffer.AsSpan(offset+sent,count-sent).ToArray();var n=send(s,bytes,bytes.Length,0);if(n<=0)throw new IOException("蓝牙发送失败");sent+=n;}}
        public override void Flush(){}public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token)=>Task.Run(()=>Read(buffer,offset,count),token);
        public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token)=>Task.Run(()=>Write(buffer,offset,count),token);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            var bytes=new byte[buffer.Length];var count=await ReadAsync(bytes,0,bytes.Length,token);bytes.AsMemory(0,count).CopyTo(buffer);return count;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default)
        {
            var bytes=buffer.ToArray();return new ValueTask(WriteAsync(bytes,0,bytes.Length,token));
        }
        public override Task FlushAsync(CancellationToken token)=>Task.CompletedTask;
        protected override void Dispose(bool disposing){var old=Interlocked.Exchange(ref s,new IntPtr(-1));if(old!=new IntPtr(-1)){shutdown(old,2);closesocket(old);}base.Dispose(disposing);}
    }
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct BthAddress{public ushort Family;public ulong Address;public Guid Service;public uint Port;}
    [StructLayout(LayoutKind.Sequential)]struct SocketAddress{public IntPtr Address;public int Length;}
    [StructLayout(LayoutKind.Sequential)]struct CsAddress{public SocketAddress Local,Remote;public int SocketType,Protocol;}
    [StructLayout(LayoutKind.Sequential)]struct QuerySet{public int Size;public IntPtr ServiceInstanceName,ServiceClassId,Version,Comment;public int Namespace;public IntPtr NsProviderId,Context;public int NumberOfProtocols;public IntPtr AfpProtocols,QueryString;public int NumberOfCsAddrs;public IntPtr CsBuffer;public int OutputFlags;public IntPtr Blob;}
    [DllImport("ws2_32.dll")]static extern int WSAStartup(ushort version,[Out]byte[] data);
    [DllImport("ws2_32.dll")]static extern IntPtr socket(int family,int type,int protocol);
    [DllImport("ws2_32.dll")]static extern int bind(IntPtr socket,ref BthAddress address,int size);
    [DllImport("ws2_32.dll")]static extern int getsockname(IntPtr socket,ref BthAddress address,ref int size);
    [DllImport("ws2_32.dll")]static extern int listen(IntPtr socket,int backlog);
    [DllImport("ws2_32.dll")]static extern IntPtr accept(IntPtr socket,IntPtr address,IntPtr size);
    [DllImport("ws2_32.dll")]static extern int WSACleanup();
    [DllImport("ws2_32.dll")]static extern int setsockopt(IntPtr socket,int level,int name,ref int value,int length);
    [DllImport("ws2_32.dll",EntryPoint="WSASetServiceW",CharSet=CharSet.Unicode)]static extern int WSASetService(ref QuerySet query,int operation,uint flags);
    [DllImport("ws2_32.dll")]static extern int WSAGetLastError();
    [DllImport("ws2_32.dll")]static extern int closesocket(IntPtr socket);
    [DllImport("ws2_32.dll")]static extern int shutdown(IntPtr socket,int how);
    [DllImport("ws2_32.dll")]static extern int recv(IntPtr socket,[Out]byte[] buffer,int length,int flags);
    [DllImport("ws2_32.dll")]static extern int send(IntPtr socket,byte[] buffer,int length,int flags);
}
