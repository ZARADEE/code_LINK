using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;

internal static class ConsoleTerminal
{
    public static void Enable()
    {
        var output=GetStdHandle(-11);if(GetConsoleMode(output,out var mode))SetConsoleMode(output,mode|4);
        Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=Encoding.UTF8;
    }
    public static string Keys(ConsoleKeyInfo key)
    {
        if(key.Key==ConsoleKey.Enter)return "\r";
        if(key.Key==ConsoleKey.Backspace)return "\u007f";
        if(key.KeyChar!='\0')return key.Modifiers.HasFlag(ConsoleModifiers.Alt)?"\u001b"+key.KeyChar:key.KeyChar.ToString();
        var modifier=1+(key.Modifiers.HasFlag(ConsoleModifiers.Shift)?1:0)+(key.Modifiers.HasFlag(ConsoleModifiers.Alt)?2:0)+(key.Modifiers.HasFlag(ConsoleModifiers.Control)?4:0);
        var arrow=key.Key switch{ConsoleKey.UpArrow=>"A",ConsoleKey.DownArrow=>"B",ConsoleKey.RightArrow=>"C",ConsoleKey.LeftArrow=>"D",ConsoleKey.Home=>"H",ConsoleKey.End=>"F",_=>""};
        if(arrow.Length>0)return modifier==1?"\u001b["+arrow:"\u001b[1;"+modifier+arrow;
        return key.Key switch{ConsoleKey.Delete=>"\u001b[3~",ConsoleKey.Insert=>"\u001b[2~",ConsoleKey.PageUp=>"\u001b[5~",ConsoleKey.PageDown=>"\u001b[6~",ConsoleKey.F1=>"\u001bOP",ConsoleKey.F2=>"\u001bOQ",ConsoleKey.F3=>"\u001bOR",ConsoleKey.F4=>"\u001bOS",_=>""};
    }
    public static string Quote(string argument)
    {
        if(argument.Length>0&&!argument.Any(c=>char.IsWhiteSpace(c)||c=='"'))return argument;
        var b=new StringBuilder("\"");int slashes=0;
        foreach(var c in argument){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1).Append(c);slashes=0;continue;}b.Append('\\',slashes).Append(c);slashes=0;}
        return b.Append('\\',slashes*2).Append('"').ToString();
    }
    public static void NewWindow(string[] args,string cwd)
    {
        var command=new StringBuilder(string.Join(" ",new[]{Environment.ProcessPath!}.Concat(args).Select(Quote)));
        var startup=new StartupInfo{cb=Marshal.SizeOf<StartupInfo>()};
        if(!CreateProcess(null,command,IntPtr.Zero,IntPtr.Zero,false,0x10,IntPtr.Zero,cwd,ref startup,out var info))throw new Win32Exception();
        CloseHandle(info.hThread);CloseHandle(info.hProcess);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct StartupInfo{public int cb;public string? lpReserved,lpDesktop,lpTitle;public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public ushort wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
    [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public IntPtr hProcess,hThread;public uint processId,threadId;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string? app,StringBuilder command,IntPtr ps,IntPtr ts,bool inherit,uint flags,IntPtr env,string cwd,ref StartupInfo si,out ProcessInfo pi);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")]static extern bool GetConsoleMode(IntPtr handle,out uint mode);
    [DllImport("kernel32.dll")]static extern bool SetConsoleMode(IntPtr handle,uint mode);
}
