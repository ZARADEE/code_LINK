using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodeLink;
public sealed class TerminalProcess : IDisposable
{
    private IntPtr pseudo, process, attributes;
    private FileStream? input,output;
    public int ProcessId { get; private set; }
    public uint ExitCode { get; private set; }
    public event Action<string>? Data;
    public event Action? Exited;
    private readonly SemaphoreSlim writeLock=new(1);
    public void Start(string command,string cwd)
    {
        if(!CreatePipe(out var inRead,out var inWrite,IntPtr.Zero,0)||!CreatePipe(out var outRead,out var outWrite,IntPtr.Zero,0))throw new Win32Exception();
        try
        {
            var hr=CreatePseudoConsole(new Coord{X=120,Y=32},inRead,outWrite,0,out pseudo);if(hr!=0)Marshal.ThrowExceptionForHR(hr);
            nuint size=0;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);attributes=Marshal.AllocHGlobal((int)size);
            if(!InitializeProcThreadAttributeList(attributes,1,0,ref size))throw new Win32Exception();
            if(!UpdateProcThreadAttribute(attributes,0,(nuint)0x00020016,pseudo,(nuint)IntPtr.Size,IntPtr.Zero,IntPtr.Zero))throw new Win32Exception();
            var si=new StartupInfoEx{StartupInfo=new StartupInfo{cb=Marshal.SizeOf<StartupInfoEx>()},lpAttributeList=attributes};
            if(!CreateProcess(null,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,0x00080000|0x00000400,IntPtr.Zero,cwd,ref si,out var pi))throw new Win32Exception();
            process=pi.hProcess;ProcessId=checked((int)pi.processId);CloseHandle(pi.hThread);
            input=new FileStream(new SafeFileHandle(inWrite,true),FileAccess.Write,4096,false);inWrite=IntPtr.Zero;
            output=new FileStream(new SafeFileHandle(outRead,true),FileAccess.Read,4096,false);outRead=IntPtr.Zero;
            _=Task.Run(async()=>{var decoder=Encoding.UTF8.GetDecoder();var buffer=new byte[4096];var chars=new char[8192];try{while(true){var count=await output.ReadAsync(buffer);if(count==0)break;var n=decoder.GetChars(buffer,0,count,chars,0);Data?.Invoke(new string(chars,0,n));}}catch(IOException){}catch(ObjectDisposedException){}});
            var processHandle=process;
            _=Task.Run(()=>{WaitForSingleObject(processHandle,0xffffffff);if(GetExitCodeProcess(processHandle,out var code))ExitCode=code;else ExitCode=1;Exited?.Invoke();});
        }
        finally{CloseHandle(inRead);CloseHandle(outWrite);if(inWrite!=IntPtr.Zero)CloseHandle(inWrite);if(outRead!=IntPtr.Zero)CloseHandle(outRead);}
    }
    public async Task Write(string text){await writeLock.WaitAsync();try{if(input is null)throw new IOException("终端未运行");await input.WriteAsync(Encoding.UTF8.GetBytes(text));await input.FlushAsync();}finally{writeLock.Release();}}
    public void Resize(short cols,short rows){if(pseudo!=IntPtr.Zero)ResizePseudoConsole(pseudo,new Coord{X=cols,Y=rows});}
    public void Dispose(){if(process!=IntPtr.Zero){TerminateProcess(process,0);CloseHandle(process);process=IntPtr.Zero;}input?.Dispose();output?.Dispose();if(pseudo!=IntPtr.Zero){ClosePseudoConsole(pseudo);pseudo=IntPtr.Zero;}if(attributes!=IntPtr.Zero){DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);attributes=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)]struct Coord{public short X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct StartupInfo{public int cb;public string? lpReserved,lpDesktop,lpTitle;public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public ushort wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
    [StructLayout(LayoutKind.Sequential)]struct StartupInfoEx{public StartupInfo StartupInfo;public IntPtr lpAttributeList;}
    [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public IntPtr hProcess,hThread;public uint processId,threadId;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out IntPtr read,out IntPtr write,IntPtr security,uint size);
    [DllImport("kernel32.dll")]static extern int CreatePseudoConsole(Coord size,IntPtr input,IntPtr output,uint flags,out IntPtr result);
    [DllImport("kernel32.dll")]static extern int ResizePseudoConsole(IntPtr handle,Coord size);
    [DllImport("kernel32.dll")]static extern void ClosePseudoConsole(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref nuint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,nuint attribute,IntPtr value,nuint size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")]static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool CreateProcess(string? app,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr env,string cwd,ref StartupInfoEx info,out ProcessInfo result);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]static extern bool TerminateProcess(IntPtr handle,uint code);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
}
