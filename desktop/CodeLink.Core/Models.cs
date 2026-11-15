using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeLink;

public static class J
{
    public static string S(this JsonNode? n, string key, string fallback = "") => n?[key]?.ToString() ?? fallback;
    public static bool B(this JsonNode? n, string key, bool fallback = false) => bool.TryParse(n?[key]?.ToString(), out var b) ? b : fallback;
    public static string Stringify(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    public static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
}

public sealed class DeviceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Revoked { get; set; }
}
public sealed class HostConfig
{
    public int Port { get; set; } = 47631;
    public string CertificatePassword { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public List<DeviceRecord> Devices { get; set; } = [];
    public string CodexPath { get; set; } = "";
    public bool LanEnabled { get; set; }
    public bool BluetoothEnabled { get; set; } = true;
}
public sealed class HostStorage
{
    private readonly object gate = new();
    public string DirectoryPath { get; }
    public HostConfig Config { get; }
    public X509Certificate2 Certificate { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
    public HostStorage(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(DirectoryPath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var file = Path.Combine(DirectoryPath, "host.json");
        Config = File.Exists(file) ? JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(file))! : new();
        Config.CertificatePassword=WindowsSecret.Unprotect(Config.CertificatePassword);
        foreach(var device in Config.Devices)device.Key=WindowsSecret.Unprotect(device.Key);
        var pfx = Path.Combine(DirectoryPath, "host.pfx");
        if (!File.Exists(pfx))
        {
            using var key = RSA.Create(2048);
            var req = new CertificateRequest("CN=Code Link Local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
            File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, Config.CertificatePassword));
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(pfx,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        }
        Certificate = X509CertificateLoader.LoadPkcs12FromFile(pfx, Config.CertificatePassword,X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.PersistKeySet);
        Save();
    }
    public void Save()
    {
        lock(gate)
        {
            var secured=JsonSerializer.Deserialize<HostConfig>(JsonSerializer.Serialize(Config))!;
            secured.CertificatePassword=WindowsSecret.Protect(secured.CertificatePassword);
            foreach(var device in secured.Devices)device.Key=WindowsSecret.Protect(device.Key);
            var file=Path.Combine(DirectoryPath,"host.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(secured));File.Move(file+".tmp",file,true);
        }
    }
    public void Audit(object value) { lock (gate) File.AppendAllText(Path.Combine(DirectoryPath,"operations.jsonl"),J.Stringify(value)+"\n"); }
}

public sealed class Approval
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Method { get; set; } = "";
    public JsonNode RawId { get; set; } = null!;
    public JsonNode Parameters { get; set; } = null!;
    public bool Resolving { get; set; }
    public string Summary => Method.Contains("commandExecution") ? Parameters.S("command", "命令执行申请") : Method.Contains("fileChange") ? "文件变更申请" : Method.Contains("permissions") ? "文件或网络权限申请" : Method.Contains("requestUserInput") ? "需要你的回答" : "工具交互申请";
}
public sealed class ObserverInput
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Text {get;init;}="";
    public bool Raw {get;init;}
    public string DeviceId {get;init;}="";
    public bool Claimed {get;set;}
    public DateTime Expires {get;init;}=DateTime.UtcNow.AddSeconds(15);
    public TaskCompletionSource<string> Completion {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新会话";
    public string Kind { get; set; } = "codex";
    public string Cwd { get; set; } = Environment.CurrentDirectory;
    public string Status { get; set; } = "idle";
    public string ThreadId { get; set; } = "";
    public string TurnId { get; set; } = "";
    public string Controller { get; set; } = "pc";
    public int FrontendPid { get; set; }
    public int ObservedProcessId { get; set; }
    public long ObservedWindowHandle { get; set; }
    public WindowActivity Activity { get; } = new();
    public HashSet<string> Devices { get; set; } = [];
    public List<Approval> Approvals { get; } = [];
    public string Output { get; private set; } = "";
    public long OutputRevision { get; private set; }
    public Queue<string> InputQueue { get; } = [];
    public List<ObserverInput> ObserverInputs { get; } = [];
    public TerminalProcess? Terminal { get; set; }
    public object Gate { get; } = new();
    public void Append(string text) { lock(Gate) { Output += text; if(Output.Length>120000)Output=Output[^100000..]; OutputRevision++; } }
    public void ReplaceOutput(string text) { lock(Gate) { Output=text.Length>120000?text[^120000..]:text;OutputRevision++; } }
    public object[] PendingInputs(){lock(Gate)return ObserverInputs.Where(input=>!input.Claimed&&input.Expires>DateTime.UtcNow).Select(input=>(object)new{sessionId=Id,processId=ObservedProcessId,windowHandle=ObservedWindowHandle,input.Id,input.Text,input.Raw}).ToArray();}
    public object Snapshot(bool includeOutput) { lock(Gate) return new { Id, Name, Kind, Cwd, Status, ThreadId, TurnId, ActivityCycle=Activity.Cycle, ActivityRevision=Activity.Revision, StatusSource=Kind=="observer"?Activity.Source:"protocol", FrontendPid, ObservedProcessId, ObservedWindowHandle, ProcessId=Terminal?.ProcessId??0, Controller, Devices = Devices.ToArray(), Queued=InputQueue.Count, OutputRevision, Output=includeOutput?Output:"", approvals=Approvals.Select(a=>new {a.Id,a.Method,a.Summary,a.Parameters,a.Resolving}).ToArray() }; }
}

public sealed class Peer : IDisposable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Correlates a desktop USB setup attempt with the authenticated mobile hello.
    // It grants no authority and is only exposed to local admin clients.
    public string PairingAttempt { get; set; } = "";
    public bool Admin { get; set; }
    public string Transport { get; set; } = "";
    public string Watch { get; set; } = "";
    public long WatchGeneration;
    public Stream Stream { get; }
    public SemaphoreSlim WriteLock { get; } = new(1);
    public Peer(Stream stream) { Stream=stream; }
    public void Dispose() => Stream.Dispose();
}
