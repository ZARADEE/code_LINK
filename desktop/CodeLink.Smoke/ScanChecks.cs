using System.Diagnostics;
using CodeLink;

namespace CodeLink.Smoke;

internal static class ScanChecks
{
    public static void Run(string[] args)
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        CommandProcess[] processes = [new(1, 0, "WindowsTerminal"), new(2, 0, "cmd"), new(3, 2, "node"),
            new(4, 3, "codex"), new(5, 4, "codex"), new(6, 0, "CodeLink.Host"), new(7, 6, "powershell"),
            new(8, 7, "cmd"), new(9, 0, "powershell"), new(10, 11, "bash"), new(11, 10, "sh")];
        CommandWindow[] windows = [new(1, 100, "Codex project A"), new(1, 101, "PowerShell project B"), new(99, 999, "already exited")];
        var result = CommandCatalog.Build(processes, windows);
        Require(result.Count(p => p.ProcessId == 1) == 2, "Two windows sharing one PID must both be listed");
        Require(result.Select(p => p.Id).Distinct().Count() == result.Count, "Selection keys must distinguish windows sharing a PID");
        Require(result.Single(p => p.ProcessId == 4).ProcessChain.Contains("cmd (2)"), "The npm Codex launcher must retain its shell lineage");
        Require(!result.Any(p => p.ProcessId is 5 or 7 or 8 or 99), "Backend children, managed descendants and exited windows must be excluded");
        Require(result.Any(p => p.ProcessId == 9), "A title-less shell must remain discoverable even if its image path cannot be read");
        Require(result.Any(p => p.ProcessId == 10), "A recycled/cyclic parent PID must not hang enumeration");
        result = CommandCatalog.Build(processes, windows, new HashSet<int> { 2 });
        Require(!result.Any(p => p.ProcessId is 2 or 3 or 4 or 5), "Excluding a managed shell must also exclude its descendants");

        var clock = Stopwatch.StartNew();
        var live = CommandCatalog.Scan();
        clock.Stop();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--expect-pid") { var pid = int.Parse(args[++i]); Require(live.Any(p => p.ProcessId == pid), $"Live PID {pid} was missed"); }
            else if (args[i] == "--expect-title") { var title = args[++i]; Require(live.Any(p => p.WindowTitle.Contains(title)), $"Live window '{title}' was missed"); }
        }
        Console.WriteLine(J.Stringify(new { passed = true, regressionChecks = 7, durationMs = clock.Elapsed.TotalMilliseconds,
            candidates = live.Select(p => new { p.Id, p.ProcessId, p.Name, p.WindowTitle, p.Kind, p.ProcessChain }) }));
    }
}
