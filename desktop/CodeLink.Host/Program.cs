using CodeLink;
RuntimeConfig.Load();
var data=Environment.GetEnvironmentVariable("CODELINK_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Code Link");
using var mutex=new Mutex(true,"Local\\CodeLink.Host.v1"+Environment.GetEnvironmentVariable("CODELINK_INSTANCE"),out var created);
if(!created)return;
try
{
    using var broker=new Broker(data);
    Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;broker.Dispose();};
    Console.WriteLine("Code Link 本地代理已启动");
    await broker.Run();
}
catch(Exception e){try{Directory.CreateDirectory(data);File.AppendAllText(Path.Combine(data,"host-errors.log"),DateTimeOffset.Now+" "+e+"\n");}catch{}Console.Error.WriteLine(e);Environment.ExitCode=1;}
