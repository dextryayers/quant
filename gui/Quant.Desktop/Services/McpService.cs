using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

public sealed class McpServer
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
    public bool Enabled { get; set; }
    public int TimeoutMs { get; set; } = 10000;
}

public sealed class McpService : IDisposable
{
    public List<McpServer> Servers { get; } = new();
    public Dictionary<string, (DateTime At, string Tools)> Cache { get; } = new();
    public string Log { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public void Dispose() { }

    public void Load(string workspaceRoot)
    {
        Servers.Clear();
        try
        {
            var path = System.IO.Path.Combine(workspaceRoot, ".quant", "mcp.json");
            if (!System.IO.File.Exists(path)) return;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("servers", out var arr)) return;
            foreach (var s in arr.EnumerateArray())
            {
                Servers.Add(new McpServer
                {
                    Name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Command = s.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "",
                    Args = s.TryGetProperty("args", out var a) ? a.GetString() ?? "" : "",
                    Enabled = s.TryGetProperty("enabled", out var e) && e.GetBoolean(),
                    TimeoutMs = s.TryGetProperty("timeoutMs", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 10000,
                });
            }
        }
        catch (Exception ex) { LastError = ex.Message; Log += ex.Message + "\n"; }
    }

    public void Save(string workspaceRoot)
    {
        try
        {
            var dir = System.IO.Path.Combine(workspaceRoot, ".quant");
            System.IO.Directory.CreateDirectory(dir);
            var arr = new List<object>();
            foreach (var s in Servers)
                arr.Add(new { name = s.Name, command = s.Command, args = s.Args, enabled = s.Enabled, timeoutMs = s.TimeoutMs });
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mcp.json"), JsonSerializer.Serialize(new { servers = arr }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public static string DryRunPayload(string tool, string argsJson)
    {
        var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = tool, arguments = argsJson } });
        return $"STDIO {tool}\n--> {call}";
    }

    public async Task<string> ListToolsAsync(McpServer server, bool useCache = true, CancellationToken ct = default)
    {
        if (!server.Enabled) return "";
        if (useCache && Cache.TryGetValue(server.Name, out var hit) && (DateTime.Now - hit.At).TotalSeconds < 60)
            return hit.Tools;
        var raw = await RpcAsync(server, new[]
        {
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } }),
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }),
        }, ct).ConfigureAwait(false);
        Cache[server.Name] = (DateTime.Now, raw);
        Log += $"[mcp] {server.Name} listed {raw.Length} chars\n";
        return raw;
    }

    public async Task<string> CallToolAsync(McpServer server, string tool, string argsJson, CancellationToken ct = default)
    {
        if (!server.Enabled) throw new InvalidOperationException($"{server.Name} disabled");
        var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = tool, arguments = argsJson } });
        var raw = await RpcAsync(server, new[]
        {
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } }),
            call,
        }, ct).ConfigureAwait(false);
        Log += $"[mcp] {server.Name}/{tool} called\n";
        return raw;
    }

    private async Task<string> RpcAsync(McpServer server, string[] lines, CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = server.Command,
                Arguments = server.Args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!proc.Start())
            {
                LastError = $"spawn failed: {server.Command}";
                return "";
            }
            foreach (var l in lines)
                await proc.StandardInput.WriteLineAsync(l.AsMemory(), ct).ConfigureAwait(false);
            proc.StandardInput.Close();
            using var timeout = new CancellationTokenSource(server.TimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var output = new StringBuilder();
            try
            {
                while (!proc.HasExited)
                {
                    var line = await proc.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
                    if (line == null) break;
                    output.AppendLine(line);
                    if (output.Length > 16000) break;
                }
            }
            catch (OperationCanceledException)
            {
                LastError = $"timeout after {server.TimeoutMs}ms: {server.Name}";
                Log += $"[mcp] {LastError}\n";
            }
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return output.ToString();
        }
        catch (Exception ex)
        {
            LastError = $"{server.Name}: {ex.Message}";
            Log += $"[mcp] {LastError}\n";
            return "";
        }
    }
}
