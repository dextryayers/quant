using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class LaunchService : IDisposable
{
    private readonly Dictionary<string, Process> _procs = new();
    public event Action<string>? Output;

    public List<LaunchProfile> Load(string workspaceRoot)
    {
        var defaults = new List<LaunchProfile>
        {
            new() { Name = "gui: avalonia", Command = "dotnet", Args = "run --project gui/Quant.Desktop", Cwd = workspaceRoot },
            new() { Name = "engine: cargo", Command = "cargo", Args = "run -p quant-engine", Cwd = Path.Combine(workspaceRoot, "engine", "quant-engine"), Port = 3737 },
        };
        try
        {
            var path = Path.Combine(workspaceRoot, ".quant", "launch.json");
            if (!File.Exists(path)) return defaults;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("profiles", out var arr)) return defaults;
            var list = new List<LaunchProfile>();
            foreach (var p in arr.EnumerateArray())
            {
                var lp = new LaunchProfile
                {
                    Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Command = p.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "",
                    Args = p.TryGetProperty("args", out var a) ? a.GetString() ?? "" : "",
                    Cwd = p.TryGetProperty("cwd", out var w) ? w.GetString() ?? workspaceRoot : workspaceRoot,
                    Port = p.TryGetProperty("port", out var pt) && pt.ValueKind == JsonValueKind.Number ? pt.GetInt32() : 0,
                };
                if (p.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in env.EnumerateObject())
                        lp.Env[kv.Name] = kv.Value.GetString() ?? "";
                }
                list.Add(lp);
            }
            return list.Count > 0 ? list : defaults;
        }
        catch { return defaults; }
    }

    public void WriteTemplate(string workspaceRoot)
    {
        var dir = Path.Combine(workspaceRoot, ".quant");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "launch.json");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
        {
          "profiles": [
            { "name": "gui: avalonia", "command": "dotnet", "args": "run --project gui/Quant.Desktop", "cwd": "." },
            { "name": "engine: cargo", "command": "cargo", "args": "run -p quant-engine", "cwd": "engine/quant-engine", "port": 3737 }
          ]
        }
        """);
    }

    public bool Start(LaunchProfile p)
    {
        try
        {
            Stop(p.Name);
            var psi = new ProcessStartInfo
            {
                FileName = p.Command,
                Arguments = p.Args,
                WorkingDirectory = string.IsNullOrEmpty(p.Cwd) ? "." : p.Cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var kv in p.Env) psi.Environment[kv.Key] = kv.Value;
            var proc = Process.Start(psi);
            if (proc == null) return false;
            _procs[p.Name] = proc;
            p.Running = true;
            Output?.Invoke($"[launch] {p.Name} pid={proc.Id}\n");
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data + "\n"); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data + "\n"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (p.Port > 0)
            {
                Task.Run(async () =>
                {
                    for (var i = 0; i < 30; i++)
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                        if (await PortOpen("127.0.0.1", p.Port).ConfigureAwait(false))
                        {
                            Output?.Invoke($"[launch] {p.Name} port {p.Port} open\n");
                            break;
                        }
                    }
                });
            }
            proc.Exited += (_, __) => { p.Running = false; Output?.Invoke($"[launch] {p.Name} exited\n"); };
            proc.EnableRaisingEvents = true;
            return true;
        }
        catch (Exception ex)
        {
            Output?.Invoke($"[launch] {p.Name} failed: {ex.Message}\n");
            return false;
        }
    }

    public void Stop(string name)
    {
        if (_procs.TryGetValue(name, out var p))
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            _procs.Remove(name);
        }
    }

    public void Restart(LaunchProfile p)
    {
        Stop(p.Name);
        Start(p);
    }

    public static async Task<bool> PortOpen(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            using var cts = new CancellationTokenSource(1500);
            await c.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        foreach (var k in _procs.Keys.ToList()) Stop(k);
    }
}
