using System.Diagnostics;
namespace CodeLink;
public static class HostLauncher
{
    public static async Task Connect(LocalClient client)
    {
        try{await client.Connect();return;}catch(TimeoutException){}catch(IOException){}
        var host=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"CodeLink.Host.exe":"CodeLink.Host");
        if(!File.Exists(host))throw new IOException("找不到 CodeLink.Host.exe，请使用完整发布目录。");
        using var process=Process.Start(new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        for(var i=0;i<5;i++)
        {
            try{await client.Connect(1500);return;}catch(TimeoutException){}catch(IOException){}
            if(process is {HasExited:true,ExitCode:not 0})throw new IOException("本地代理启动失败。请查看用户数据目录 Code Link/host-errors.log。");
            await Task.Delay(200);
        }
        throw new IOException("本地代理未响应，请检查端口和 host-errors.log。");
    }
}
