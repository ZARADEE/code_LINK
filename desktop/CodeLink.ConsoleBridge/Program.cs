using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using CodeLink;

// A separate process attaches to the target console; the WPF process never changes its console.
using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
try
{
    var message=JsonNode.Parse(await Console.In.ReadToEndAsync())!;
    var window=new IntPtr(long.Parse(message.S("windowHandle")));
    Native.GetWindowThreadProcessId(window,out var owner);
    if(owner!=int.Parse(message.S("processId")))throw new IOException("目标终端窗口已关闭或已改变");
    var title=Native.WindowTitle(window);
    var candidates=new Dictionary<long,(int Pid,string Title)>();
    foreach(var process in Process.GetProcesses())using(process)
    {
        try
        {
            if(process.ProcessName.ToLowerInvariant() is not ("cmd" or "powershell" or "pwsh" or "codex" or "clink" or "wsl" or "bash"))continue;
            Native.FreeConsole();if(!Native.AttachConsole((uint)process.Id))continue;
            var consoleTitle=Native.ConsoleTitle();var console=Native.GetConsoleWindow().ToInt64();
            if(Normalize(consoleTitle)==Normalize(title))candidates.TryAdd(console,(process.Id,consoleTitle));
        }
        catch(InvalidOperationException){}
    }
    if(candidates.Count!=1)throw new IOException(candidates.Count==0?"未找到该窗口的控制台输入通道；请保持目标标签页选中":"多个终端使用相同标题，请先为目标终端设置不同标题");
    var candidate=candidates.Single();Native.FreeConsole();
    if(!Native.AttachConsole((uint)candidate.Value.Pid)||Native.GetConsoleWindow().ToInt64()!=candidate.Key||Normalize(Native.WindowTitle(window))!=Normalize(Native.ConsoleTitle()))throw new IOException("终端标签页已变化，输入未发送");
    var handle=Native.CreateFileW("CONIN$",0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
    if(handle==new IntPtr(-1))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        var text=message.S("text")+(message.B("raw")?"":"\r");var records=new List<Native.Record>();
        void Key(char character,ushort key,uint modifiers=0)
        {
            records.Add(new(){Type=1,Down=1,Repeat=1,VirtualKey=key,Character=character,Modifiers=modifiers});
            records.Add(new(){Type=1,Down=0,Repeat=1,VirtualKey=key,Character=character,Modifiers=modifiers});
        }
        if(message.S("operation")=="pointer")
        {
            var row=int.Parse(message.S("row"));var column=int.Parse(message.S("column"));
            if(!Native.GetConsoleMode(handle,out var mode)||(mode&2)!=0||(mode&0x10)==0)throw new IOException("该终端程序尚未启用鼠标交互。");
            var screen=Native.CreateFileW("CONOUT$",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
            try
            {
                if(!Native.GetConsoleScreenBufferInfo(screen,out var info)||row<0||column<0||row>info.Bottom-info.Top||column>info.Right-info.Left)throw new IOException("终端尺寸已变化，请等待更新后重新点击。");
                var line=new StringBuilder(info.Right-info.Left+1);
                if(!Native.ReadConsoleOutputCharacterW(screen,line,(uint)line.Capacity,new Native.Coord{X=info.Left,Y=(short)(info.Top+row)},out _))throw new IOException("终端画面已变化，请等待更新后重新点击。");
                // The UIA and console APIs represent trailing padding differently.
                if(line.ToString().TrimEnd()!=message.S("expectedLine").TrimEnd())throw new IOException("终端画面已变化，请等待更新后重新点击。");
                if((mode&0x200)!=0)
                {
                    // VT readers consume SGR reports. Win32 readers consume mouse records.
                    var report=$"\u001b[<0;{column+1};{row+1}M\u001b[<0;{column+1};{row+1}m";
                    foreach(var character in report)Key(character,0);
                }
                else
                {
                    records.Add(new(){Type=2,MouseX=(short)(info.Left+column),MouseY=(short)(info.Top+row),Buttons=1});
                    records.Add(new(){Type=2,MouseX=(short)(info.Left+column),MouseY=(short)(info.Top+row),Buttons=0});
                }
                if(!Native.WriteConsoleInputW(handle,records.ToArray(),(uint)records.Count,out var sent)||sent!=records.Count)throw new IOException("终端点击未完整送达。");
                output.WriteLine(J.Stringify(new{ok=true,processId=candidate.Value.Pid}));return;
            }
            finally{Native.CloseHandle(screen);}
        }
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(c=='\u001b'&&i+2<text.Length&&text[i+1]=='[')
            {
                var arrow=text[i+2] switch{'A'=>0x26,'B'=>0x28,'C'=>0x27,'D'=>0x25,_=>0};
                if(arrow>0){Key('\0',(ushort)arrow,0x100);i+=2;continue;}
            }
            if(c=='\u0003'){Key(c,0x43,8);continue;}
            var key=c switch{'\r' or '\n'=>0x0d,'\t'=>9,'\u001b'=>0x1b,'\b' or '\u007f'=>8,_=>0};
            Key(c=='\n'?'\r':c=='\u007f'?'\b':c,(ushort)key);
            if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;
        }
        if(!Native.WriteConsoleInputW(handle,records.ToArray(),(uint)records.Count,out var written)||written!=records.Count)throw new IOException("终端输入未完整送达，请检查窗口状态");
        output.WriteLine(J.Stringify(new{ok=true,processId=candidate.Value.Pid,characters=text.Length}));
    }
    finally{Native.CloseHandle(handle);Native.FreeConsole();}
}
catch(Exception e){output.WriteLine(J.Stringify(new{ok=false,error=e.Message}));Environment.ExitCode=1;}
static string Normalize(string value)=>value.Trim().TrimStart(Enumerable.Range(0x2800,256).Select(i=>(char)i).ToArray()).Trim();

static class Native
{
    // INPUT_RECORD has a WORD tag, 2-byte padding and a 16-byte event union.
    [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=20)]public struct Record
    {
        [FieldOffset(0)]public ushort Type;[FieldOffset(4)]public int Down;[FieldOffset(8)]public ushort Repeat;
        [FieldOffset(10)]public ushort VirtualKey;[FieldOffset(12)]public ushort Scan;
        [FieldOffset(14)]public char Character;[FieldOffset(16)]public uint Modifiers;
        [FieldOffset(4)]public short MouseX;[FieldOffset(6)]public short MouseY;[FieldOffset(8)]public uint Buttons;
        [FieldOffset(12)]public uint MouseModifiers;[FieldOffset(16)]public uint MouseFlags;
    }
    [StructLayout(LayoutKind.Sequential)]public struct Coord{public short X,Y;}
    [StructLayout(LayoutKind.Sequential)]public struct ScreenInfo{public Coord Size,Cursor;public ushort Attributes;public short Left,Top,Right,Bottom;public Coord Maximum;}
    public static string ConsoleTitle(){var buffer=new StringBuilder(4096);GetConsoleTitleW(buffer,buffer.Capacity);return buffer.ToString();}
    public static string WindowTitle(IntPtr window){var buffer=new StringBuilder(4096);GetWindowTextW(window,buffer,buffer.Capacity);return buffer.ToString();}
    [DllImport("kernel32.dll")]public static extern bool FreeConsole();
    [DllImport("kernel32.dll",SetLastError=true)]public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")]public static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern uint GetConsoleTitleW(StringBuilder title,int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowTextW(IntPtr window,StringBuilder title,int length);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr window,out int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]public static extern IntPtr CreateFileW(string file,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]public static extern bool WriteConsoleInputW(IntPtr input,Record[] records,uint count,out uint written);
    [DllImport("kernel32.dll")]public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]public static extern bool GetConsoleMode(IntPtr handle,out uint mode);
    [DllImport("kernel32.dll")]public static extern bool GetConsoleScreenBufferInfo(IntPtr handle,out ScreenInfo info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern bool ReadConsoleOutputCharacterW(IntPtr handle,StringBuilder text,uint length,Coord position,out uint read);
}
