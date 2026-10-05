using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

/// V0.1.0 integrated terminal: persistent shell per session with streaming output.
/// Uses cmd on Windows, sh elsewhere. No pty yet, full pty lands after V0.1.0.
public sealed class TerminalSession : IDisposable
{
    private Process? _proc;
    public string Id { get; } = Guid.NewGuid().ToString("N")[..6];
    public string Cwd { get; }
    public StringBuilder Output { get; } = new();
    public bool Running => _proc != null && !_proc.HasExited;
    public event Action? Changed;

    public TerminalSession(string cwd)
    {
        Cwd = cwd;
        Output.AppendLine($"quant terminal [{Id}] cwd={cwd}");
        Output.AppendLine("Type commands below. Output streams here. Long tasks cancellable via Kill.");
    }

    public void Send(string command, CancellationToken ct = default)
    {
        Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd" : "sh",
                    Arguments = OperatingSystem.IsWindows() ? $"/C {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = Cwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                _proc = Process.Start(psi);
                if (_proc == null) return;
                Output.AppendLine($"$ {command}");
                Changed?.Invoke();
                var stdout = await _proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                var stderr = await _proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                await _proc.WaitForExitAsync(ct).ConfigureAwait(false);
                var tail = (stdout + stderr);
                if (tail.Length > 64000) tail = tail[^64000..];
                Output.AppendLine(tail.TrimEnd());
                Output.AppendLine($"[exit {_proc.ExitCode}]");
            }
            catch (Exception ex)
            {
                Output.AppendLine($"[error {ex.Message}]");
            }
            Changed?.Invoke();
        }, ct);
    }

    public void Kill()
    {
        try
        {
            if (_proc != null && !_proc.HasExited)
            {
                _proc.Kill(true);
                Output.AppendLine("[killed]");
                Changed?.Invoke();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { Kill(); _proc?.Dispose(); } catch { }
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
