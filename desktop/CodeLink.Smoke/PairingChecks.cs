using CodeLink;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CodeLink.Smoke;

internal static class PairingChecks
{
    public static async Task Run()
    {
        Environment.SetEnvironmentVariable("CODELINK_INSTANCE", "pairing-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(Environment.CurrentDirectory, ".local", "pairing-check-" + Guid.NewGuid().ToString("N"));
        using var broker = new Broker(directory);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        broker.Storage.Config.Port = port;
        broker.Storage.Config.BluetoothEnabled = false;
        broker.Storage.Config.LanEnabled = false;
        var running = broker.Run();
        try
        {
            using var admin = new LocalClient();
            await admin.Connect(5000);
            await Wait(() => admin.CurrentState is not null);
            var code = admin.CurrentState!["pairing"]!.S("code");
            var attempt = Guid.NewGuid().ToString("N");
            using var writer = new SemaphoreSlim(1);

            async Task<SslStream> Connect()
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert is not null &&
                    Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(broker.Storage.Fingerprint, StringComparison.OrdinalIgnoreCase));
                await ssl.AuthenticateAsClientAsync("Code Link Local");
                return ssl;
            }

            using (var denied = await Connect())
            {
                await Wire.Write(denied, new { type = "hello", deviceId = "denied", code = "invalid", pairingAttempt = attempt }, writer);
                var response = await Wire.Read(denied).WaitAsync(TimeSpan.FromSeconds(10));
                Require(response?.S("type") == "error", "An attempt marker bypassed authentication");
                Require(!admin.CurrentState!["devices"]!.AsArray().Any(d => d.S("pairingAttempt") == attempt), "Unverified setup appeared successful");
            }
            string key;
            using (var mobile = await Connect())
            {
                await Wire.Write(mobile, new { type = "hello", deviceId = "phone", name = "Phone", code, pairingAttempt = attempt }, writer);
                var welcome = await Wire.Read(mobile).WaitAsync(TimeSpan.FromSeconds(10));
                Require(welcome?.S("type") == "welcome", "Mobile authentication failed");
                key = welcome!.S("key");
                var snapshot = await Wire.Read(mobile).WaitAsync(TimeSpan.FromSeconds(10));
                Require(snapshot?["devices"] is null, "Mobile peer received admin setup details");
                await Wait(() => admin.CurrentState!["devices"]!.AsArray().Any(d => d.S("id") == "phone" && d.B("online") && d.S("pairingAttempt") == attempt));
            }
            await Wait(() => admin.CurrentState!["devices"]!.AsArray().All(d => !d.B("online")));
            using (var legacy = await Connect())
            {
                await Wire.Write(legacy, new { type = "hello", deviceId = "phone", key }, writer);
                Require((await Wire.Read(legacy).WaitAsync(TimeSpan.FromSeconds(10)))?.S("type") == "welcome", "Legacy paired client cannot reconnect");
                await Wait(() => admin.CurrentState!["devices"]!.AsArray().Any(d => d.S("id") == "phone" && d.B("online") && d.S("pairingAttempt") == ""));
            }
            Console.WriteLine("PASS: authenticated setup confirmation, denied false success, admin-only marker, legacy reconnect without stale confirmation");
        }
        finally
        {
            broker.Dispose();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            broker.Storage.Certificate.Dispose();
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Wait(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(100); }
        throw new TimeoutException("Expected pairing state did not arrive");
    }
}
