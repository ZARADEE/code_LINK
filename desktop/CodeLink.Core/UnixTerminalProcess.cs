using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace CodeLink;

// forkpty lives in a separate native process, never in the multithreaded CLR.
internal sealed class UnixTerminalProcess : IDisposable
{
    private Process? process;
    private readonly SemaphoreSlim writer=new(1);
    public int ProcessId=>process?.Id??0;
    public uint ExitCode{get;private set;}=1;
    public event Action<string>? Data;
    public event Action? Exited;
    public void Start(string command,string cwd)
    {
        var helper=Path.Combine(AppContext.BaseDirectory,"codelink-pty");
        if(!File.Exists(helper))throw new FileNotFoundException("The macOS terminal helper is missing. Use the complete app bundle.",helper);
        var info=new ProcessStartInfo(helper){WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(command);info.Environment["TERM"]="xterm-256color";
        process=Process.Start(info)??throw new IOException("Could not start terminal");
        var current=process;
        _=Task.Run(async()=>
        {
            var output=Task.Run(async()=>{var buffer=new char[4096];int count;while((count=await current.StandardOutput.ReadAsync(buffer))>0)Data?.Invoke(new string(buffer,0,count));});
            var error=Task.Run(async()=>{var text=await current.StandardError.ReadToEndAsync();if(text.Length>0)Data?.Invoke(text);});
            try{await current.WaitForExitAsync();await Task.WhenAll(output,error);ExitCode=unchecked((uint)current.ExitCode);Exited?.Invoke();}
            catch(ObjectDisposedException){}
        });
    }
    private async Task Send(byte kind,byte[] data)
    {
        await writer.WaitAsync();
        try
        {
            if(process is null||process.HasExited)throw new IOException("Terminal has exited");
            var header=new byte[5];header[0]=kind;BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1),data.Length);
            await process.StandardInput.BaseStream.WriteAsync(header);await process.StandardInput.BaseStream.WriteAsync(data);await process.StandardInput.BaseStream.FlushAsync();
        }
        finally{writer.Release();}
    }
    public Task Write(string text)=>Send(1,Encoding.UTF8.GetBytes(text));
    public void Resize(short cols,short rows)
    {
        var data=new byte[4];BinaryPrimitives.WriteInt16BigEndian(data,cols);BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2),rows);
        Send(2,data).GetAwaiter().GetResult();
    }
    public void Dispose()
    {
        var current=process;if(current is null)return;
        try{if(!current.HasExited){Send(3,[]).GetAwaiter().GetResult();if(!current.WaitForExit(2000))current.Kill(true);}}
        catch(InvalidOperationException){}catch(IOException){}
        // The asynchronous output reader owns the remaining process lifetime.
    }
}
