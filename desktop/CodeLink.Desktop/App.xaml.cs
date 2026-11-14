using System.Windows;
using System;
using System.Threading;
using System.Security.Principal;
namespace CodeLink.Desktop;
public partial class App:Application
{
    private EventWaitHandle? activation;
    public App(){CodeLink.RuntimeConfig.Load();}
    protected override void OnStartup(StartupEventArgs e)
    {
        var name="Local\\CodeLink.Show."+WindowsIdentity.GetCurrent().User!.Value+"."+(Environment.GetEnvironmentVariable("CODELINK_INSTANCE")??"default");
        activation=new EventWaitHandle(false,EventResetMode.AutoReset,name,out var first);
        if(!first){activation.Set();activation.Dispose();activation=null;Shutdown();return;}
        var signal=activation;
        new Thread(()=>
        {
            try{while(signal.WaitOne())Dispatcher.BeginInvoke(new Action(()=>{if(MainWindow is MainWindow panel)panel.RestorePanel();}));}
            catch(ObjectDisposedException){}
        }){IsBackground=true,Name="CodeLink panel activation"}.Start();
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e){activation?.Dispose();base.OnExit(e);}
}
