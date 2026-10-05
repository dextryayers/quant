using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

/// Full interactive shell session. One persistent process per session:
/// stdin stays open, cd and env persist, output streams live.
/// Windows: PowerShell (pwsh, powershell) or CMD. Unix: $SHELL, bash, sh.
public sealed class TerminalSession : IDisposable
{
    private Process? _proc;
    private StreamWriter? _stdin;
    private readonly object _gate = new();
    private bool _disposed;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..6];
    public string Cwd { get; }
    public string ShellExe { get; }
    public string ShellShort { get; }
    public StringBuilder Output { get; } = new();
    public bool Running => _proc != null && !_proc.HasExited;
    public string Label => $"{ShellShort} · {Id}";
    public event Action? Changed;

    private const int MaxChars = 131072;

    public TerminalSession(string cwd, string? shell = null)
    {
        Cwd = cwd;
        (ShellExe, ShellShort) = ResolveShell(shell);
        Output.AppendLine($"— {ShellShort} — {Cwd}");
        Output.AppendLine("Type a command below and press Enter. cd and env persist in this session.");
        Start();
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static List<(string Exe, string Short)> AvailableShells()
    {
        var list = new List<(string Exe, string Short)>();
        if (OperatingSystem.IsWindows())
        {
            var pwsh = Which("pwsh.exe");
            if (pwsh != null) list.Add((pwsh, "PS7"));
            var ps = Which("powershell.exe");
            if (ps != null) list.Add((ps, "PS"));
            var cmd = Which("cmd.exe");
            if (cmd != null) list.Add((cmd, "CMD"));
            if (list.Count == 0) list.Add(("cmd.exe", "CMD"));
        }
        else
        {
            var sh = Environment.GetEnvironmentVariable("SHELL");
            if (!string.IsNullOrWhiteSpace(sh) && File.Exists(sh))
                list.Add((sh, ShortFor(sh)));
            foreach (var cand in new[] { "/bin/bash", "/bin/zsh", "/bin/sh" })
            {
                if (File.Exists(cand) && list.TrueForAll(s => s.Exe != cand))
                    list.Add((cand, ShortFor(cand)));
            }
            if (list.Count == 0) list.Add(("sh", "sh"));
        }
        return list;
    }

    private static (string Exe, string Short) ResolveShell(string? want)
    {
        var all = AvailableShells();
        if (!string.IsNullOrWhiteSpace(want))
        {
            foreach (var (exe, shortName) in all)
            {
                var file = Path.GetFileName(exe);
                if (file.StartsWith(want, StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                    shortName.Equals(want, StringComparison.OrdinalIgnoreCase))
                    return (exe, shortName);
            }
        }
        return all[0];
    }

    private static string ShortFor(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        return name switch
        {
            "pwsh" => "PS7",
            "powershell" => "PS",
            "cmd" => "CMD",
            "bash" => "bash",
            "zsh" => "zsh",
            "fish" => "fish",
            "sh" => "sh",
            _ => name,
        };
    }

    private static string? Which(string exe)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var full = Path.Combine(dir.Trim(), exe);
                if (File.Exists(full)) return full;
            }
            var sys = Environment.SystemDirectory;
            if (!string.IsNullOrEmpty(sys))
            {
                var full = Path.Combine(sys, exe);
                if (File.Exists(full)) return full;
            }
        }
        catch { }
        return null;
    }

    private void Start()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ShellExe,
                WorkingDirectory = Cwd,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (ShellShort is "PS7" or "PS")
                psi.Arguments = "-NoLogo";
            if (!OperatingSystem.IsWindows())
                psi.Environment["TERM"] = "dumb";
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            _proc.Exited += (_, __) =>
            {
                lock (_gate)
                {
                    try { Output.AppendLine($"[session ended (code {_proc?.ExitCode}) — press New for a fresh one]"); } catch { }
                }
                Changed?.Invoke();
            };
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            _stdin = _proc.StandardInput;
            _stdin.AutoFlush = true;
        }
        catch (Exception ex)
        {
            Output.AppendLine($"[could not start {ShellExe}: {ex.Message}]");
            Changed?.Invoke();
        }
    }

    /// Sends one line to the live shell, exactly like typing it + Enter.
    public void SendLine(string command)
    {
        var cmd = (command ?? "").TrimEnd();
        if (cmd.Length == 0) return;
        lock (_gate)
        {
            if (!Running || _stdin == null)
            {
                Output.AppendLine("[session ended — press New for a fresh one]");
            }
            else
            {
                try
                {
                    Output.AppendLine($"$ {cmd}");
                    _stdin.WriteLine(cmd);
                }
                catch (Exception ex)
                {
                    Output.AppendLine($"[write failed: {ex.Message}]");
                }
            }
            Trim();
        }
        Changed?.Invoke();
    }

    // Back-compat for task runner: runs inside this live session.
    public void Send(string command, CancellationToken ct = default) => SendLine(command);

    public void Clear()
    {
        lock (_gate)
        {
            Output.Clear();
            Output.AppendLine($"— {ShellShort} — {Cwd} (cleared)");
        }
        Changed?.Invoke();
    }

    public void Restart()
    {
        Kill(silent: true);
        lock (_gate) { Output.AppendLine("[restarted]"); }
        Start();
        Changed?.Invoke();
    }

    public void Kill(bool silent = false)
    {
        try
        {
            if (_proc != null && !_proc.HasExited)
            {
                _proc.Kill(true);
                if (!silent)
                {
                    lock (_gate) { Output.AppendLine("[killed]"); }
                }
                Changed?.Invoke();
            }
        }
        catch { }
    }

    private void Append(string line)
    {
        lock (_gate)
        {
            Output.AppendLine(Ansi.Strip(line));
            Trim();
        }
        Changed?.Invoke();
    }

    private void Trim()
    {
        if (Output.Length > MaxChars)
            Output.Remove(0, Output.Length - MaxChars);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Kill(silent: true); _proc?.Dispose(); } catch { }
        try { _stdin?.Dispose(); } catch { }
    }
}

/// Keeps terminal output readable: strips ANSI escapes and control noise.
/// Colors arrive as codes without a rich renderer, so plain text wins.
public static class Ansi
{
    private static readonly Regex Osc = new(@"\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)", RegexOptions.Compiled);
    private static readonly Regex Csi = new(@"\x1B\[[0-9;?]*[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex Other = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    public static string Strip(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Osc.Replace(s, "");
        s = Csi.Replace(s, "");
        s = s.Replace("\x1B=", "").Replace("\x1B>", "");
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        return Other.Replace(s, "");
    }
}

public sealed class TaskService
{
    public List<Models.DevTask> Load(string workspaceRoot)
    {
        var list = new List<Models.DevTask>
        {
            new() { Label = "build: dotnet", Command = "dotnet build Quant.slnx -v minimal", Cwd = workspaceRoot },
            new() { Label = "build: cargo", Command = "cargo build -p quant-engine", Cwd = Path.Combine(workspaceRoot, "engine", "quant-engine") },
            new() { Label = "test: cargo", Command = "cargo test -p quant-engine", Cwd = Path.Combine(workspaceRoot, "engine", "quant-engine") },
        };
        try
        {
            var path = Path.Combine(workspaceRoot, ".quant", "tasks.json");
            if (!File.Exists(path)) return list;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("tasks", out var tasks))
            {
                list.Clear();
                foreach (var t in tasks.EnumerateArray())
                {
                    list.Add(new Models.DevTask
                    {
                        Label = t.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        Command = t.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "",
                        Cwd = t.TryGetProperty("cwd", out var w) ? w.GetString() ?? workspaceRoot : workspaceRoot,
                    });
                }
            }
        }
        catch { }
        return list;
    }
}
