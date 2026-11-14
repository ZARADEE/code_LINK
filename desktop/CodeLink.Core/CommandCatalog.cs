using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeLink;

public sealed record CommandCandidate(int ProcessId, string Name, string Executable, string WindowTitle,
    long WindowHandle = 0, string Kind = "process", string ProcessChain = "")
{
    // Windows Terminal can own several windows under one PID.
    public string Id => $"{ProcessId}:{WindowHandle}";
    public string DisplayName => Name.ToLowerInvariant() switch
    {
        "windowsterminal" => "Windows Terminal", "powershell" => "PowerShell", "pwsh" => "PowerShell 7",
        "cmd" => "命令提示符", "codex" => "Codex", "wsl" => "WSL", _ => Name
    };
    public string Label => string.IsNullOrWhiteSpace(WindowTitle)
        ? $"{DisplayName} · PID {ProcessId}\n{ProcessChain}"
        : $"{WindowTitle}\n{DisplayName} 窗口 · PID {ProcessId}";
}

internal sealed record CommandProcess(int Id, int ParentId, string Name);
internal sealed record CommandWindow(int ProcessId, long Handle, string Title);

public static class CommandCatalog
{
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
        { "bash", "cmd", "fish", "git-bash", "pwsh", "powershell", "sh", "ubuntu", "wsl", "zsh" };
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
        { "codex", "claude", "gemini", "opencode", "aider" };
    private static readonly HashSet<string> TerminalHosts = new(StringComparer.OrdinalIgnoreCase)
        { "WindowsTerminal", "OpenConsole", "conhost", "mintty", "wezterm-gui", "alacritty", "kitty" };

    public static IReadOnlyList<CommandCandidate> Scan(ISet<int>? excludedProcessIds = null)
    {
        // Other platforms expose sessions launched by Code Link; Win32 window
        // enumeration must never run on macOS.
        if (!OperatingSystem.IsWindows()) return [];
        var processes = Snapshot();
        return Build(processes, Windows(processes), excludedProcessIds)
            .Select(item => item with { Executable = Executable(item.ProcessId) }).ToArray();
    }

    internal static IReadOnlyList<CommandCandidate> Build(IReadOnlyList<CommandProcess> snapshot,
        IReadOnlyList<CommandWindow> windows, ISet<int>? excludedProcessIds = null)
    {
        var processes = snapshot.ToDictionary(p => p.Id);
        IEnumerable<CommandProcess> Ancestors(CommandProcess process)
        {
            var visited = new HashSet<int> { process.Id };
            while (processes.TryGetValue(process.ParentId, out var parent) && visited.Add(parent.Id))
            { yield return parent; process = parent; }
        }
        bool Excluded(CommandProcess process) => excludedProcessIds?.Contains(process.Id) == true ||
            Ancestors(process).Any(p => excludedProcessIds?.Contains(p.Id) == true || p.Name.Equals("CodeLink.Host", StringComparison.OrdinalIgnoreCase));

        var result = new List<CommandCandidate>();
        var windowOwners = new HashSet<int>();
        foreach (var window in windows)
        {
            if (!processes.TryGetValue(window.ProcessId, out var process) || Excluded(process)) continue;
            windowOwners.Add(process.Id);
            result.Add(new(process.Id, process.Name, "", window.Title, window.Handle, "window"));
        }
        foreach (var process in snapshot)
        {
            if (Excluded(process) || windowOwners.Contains(process.Id)) continue;
            if (!Shells.Contains(process.Name) && !Commands.Contains(process.Name)) continue;
            var ancestors = Ancestors(process).ToArray();
            // Codex also launches app-server/maintenance children with the same executable name.
            if (Commands.Contains(process.Name) && ancestors.Any(p => p.Name.Equals(process.Name, StringComparison.OrdinalIgnoreCase))) continue;
            var lineage = new List<CommandProcess> { process };
            foreach (var parent in ancestors)
            {
                if (!Shells.Contains(parent.Name) && !Commands.Contains(parent.Name) &&
                    parent.Name is not ("node" or "python" or "python3" or "uv")) break;
                lineage.Add(parent);
            }
            var chain = lineage.Count > 1 ? string.Join(" ← ", lineage.Select(p => $"{p.Name} ({p.Id})")) : "命令行进程";
            result.Add(new(process.Id, process.Name, "", "", Kind: "process", ProcessChain: chain));
        }
        return result.OrderBy(p => p.Kind == "window" ? 0 : Commands.Contains(p.Name) ? 1 : 2)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProcessId).ThenBy(p => p.WindowHandle).ToArray();
    }

    private static List<CommandProcess> Snapshot()
    {
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取进程列表");
        try
        {
            var result = new List<CommandProcess>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Executable = "" };
            if (!Process32FirstW(handle, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do { result.Add(new((int)entry.ProcessId, (int)entry.ParentId, Path.GetFileNameWithoutExtension(entry.Executable))); }
            while (Process32NextW(handle, ref entry));
            return result;
        }
        finally { CloseHandle(handle); }
    }

    private static List<CommandWindow> Windows(IReadOnlyList<CommandProcess> snapshot)
    {
        var processes = snapshot.ToDictionary(p => p.Id);
        var result = new List<CommandWindow>();
        EnumWindows((handle, _) =>
        {
            // IsWindowVisible includes minimized windows; IsIconic must not discard them.
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out var pid);
            if (!processes.TryGetValue((int)pid, out var process)) return true;
            var className = new StringBuilder(256); GetClassNameW(handle, className, className.Capacity);
            if (!TerminalHosts.Contains(process.Name) && !Shells.Contains(process.Name) && !Commands.Contains(process.Name) &&
                className.ToString() is not ("ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS")) return true;
            var title = new StringBuilder(2048); GetWindowTextW(handle, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;
            result.Add(new((int)pid, handle.ToInt64(), title.ToString()));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string Executable(int pid)
    {
        var process = OpenProcess(0x1000, false, (uint)pid);
        if (process == IntPtr.Zero) return ""; // Protected processes still belong in the list.
        try
        {
            var path = new StringBuilder(32768); var length = path.Capacity;
            return QueryFullProcessImageNameW(process, 0, path, ref length) ? path.ToString() : "";
        }
        finally { CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    private delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr handle, StringBuilder text, int length);
}
