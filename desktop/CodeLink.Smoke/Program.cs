using CodeLink;
using System.Diagnostics;
using System.Text.Json.Nodes;

if(args.FirstOrDefault()=="--activity"){CodeLink.Smoke.ActivityChecks.Run();return;}
if(args.FirstOrDefault()=="--pairing-checks"){await CodeLink.Smoke.PairingChecks.Run();return;}

if(args.FirstOrDefault()=="--scan")
{
    CodeLink.Smoke.ScanChecks.Run(args.Skip(1).ToArray());return;
}
if(args.FirstOrDefault()=="--transport")
{
    await CodeLink.Smoke.TransportChecks.Run();return;
}

if(args.FirstOrDefault()=="--admin")
{
    try{await CodeLink.Smoke.Admin.Run(args.Skip(1).ToArray());}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    return;
}

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var host = Path.Combine(root, "desktop/CodeLink.Host/bin/Debug/net10.0/CodeLink.Host.exe");
if (!File.Exists(host)) throw new FileNotFoundException("Build CodeLink.Host before running this smoke test", host);

using var client = new LocalClient();
var firstState = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
client.State += state => firstState.TrySetResult(state);
try { await client.Connect(); }
catch
{
    Process.Start(new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
    await Task.Delay(1000);
    await client.Connect();
}

if (args.FirstOrDefault() == "--pairing")
{
    var state = await firstState.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var pairing = state["pairing"] ?? throw new IOException("The local broker did not publish pairing information.");
    var invite = J.Stringify(new { host = "127.0.0.1", port = int.Parse(pairing.S("port")), pin = pairing.S("fingerprint"), code = pairing.S("code") });
    var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(invite)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    Console.WriteLine("codelink:" + encoded);
    return;
}

var allOutput = new System.Text.StringBuilder();
var outputGate = new object();
client.State += state =>
{
    lock (outputGate)
    {
        var output = state.S("output");
        if (state.B("reset")) allOutput.Clear();
        allOutput.Append(output);
    }
};

if (args.FirstOrDefault() == "--mobile-terminal")
{
    var deviceId = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Pass the paired Android device ID.");
    var mobileSession = await client.Action("create", new
    {
        kind = "terminal",
        name = "Android input test",
        cwd = Environment.CurrentDirectory,
        command = "powershell.exe -NoLogo -NoProfile -Command \"$value = Read-Host 'REMOTE READY'; Write-Output ('MOBILE_RECEIVED:' + $value); Start-Sleep -Seconds 20\""
    });
    var mobileSessionId = mobileSession.S("sessionId");
    await client.Action("map", new { sessionId = mobileSessionId, deviceId, enabled = true, control = true });
    await client.Watch(mobileSessionId);
    await WaitUntil(() => { lock (outputGate) return allOutput.ToString().Contains("REMOTE READY"); }, TimeSpan.FromSeconds(15));
    Console.WriteLine("MOBILE_SESSION=" + mobileSessionId);
    Console.WriteLine("READY_FOR_MOBILE_INPUT");
    await WaitUntil(() => { lock (outputGate) return allOutput.ToString().Contains("MOBILE_RECEIVED:CODELINK_PAD_INPUT"); }, TimeSpan.FromSeconds(90));
    Console.WriteLine("Android input arrived in the mapped PowerShell session.");
    await client.Action("delete", new { sessionId = mobileSessionId });
    return;
}

var session = await client.Action("create", new
{
    kind = "terminal",
    name = "Code Link smoke",
    cwd = Environment.CurrentDirectory,
    command = "powershell.exe -NoLogo -NoProfile -Command \"$value = Read-Host 'READY'; Write-Output ('RECEIVED:' + $value); Start-Sleep -Seconds 2\""
});
var id = session.S("sessionId");
await client.Watch(id);
await WaitUntil(() => { lock (outputGate) return allOutput.ToString().Contains("READY"); }, TimeSpan.FromSeconds(10));
await client.Action("input", new { sessionId = id, text = "CODELINK_REMOTE_INPUT" });
await WaitUntil(() => { lock (outputGate) return allOutput.ToString().Contains("RECEIVED:CODELINK_REMOTE_INPUT"); }, TimeSpan.FromSeconds(10));
Console.WriteLine("Terminal pseudo-console captured output and accepted input.");
await client.Action("delete", new { sessionId = id });

static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!predicate())
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Expected terminal output did not arrive.");
        await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100));
    }
}
