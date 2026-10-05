using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

/// Ensures the Rust sidecar is running next to the GUI.
/// If localhost health fails, spawns quant-engine.exe from the app directory.
public sealed class EngineSupervisor
{
    private Process? _child;

    public async Task<bool> EnsureRunningAsync(int port, CancellationToken ct = default)
    {
        if (await Healthy(port, ct).ConfigureAwait(false)) return true;
        var exe = FindEngine();
        if (exe == null) return false;
        try
        {
            _child = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--port {port}",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { return false; }
        for (var i = 0; i < 30; i++)
        {
            if (ct.IsCancellationRequested) return false;
            if (await Healthy(port, ct).ConfigureAwait(false)) return true;
            try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch { return false; }
        }
        return false;
    }

    public static async Task<bool> Healthy(int port, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var res = await http.GetAsync($"http://127.0.0.1:{port}/health", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static string? FindEngine()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            foreach (var name in new[] { "quant-engine.exe", "quant-engine" })
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            // dev layout: gui publishes next to engine target
            var dev = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "engine", "quant-engine", "target", "debug", "quant-engine.exe"));
            if (File.Exists(dev)) return dev;
        }
        catch { }
        return null;
    }
}
