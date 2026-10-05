using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

public sealed class AgentService : IDisposable
{
    private readonly HttpClient _http;
    public AgentService(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };
    }
    public void Dispose() => _http.Dispose();

    public async Task<List<(string Type, string Tool, string Text)>> RunAsync(string mode, string message, string root, Action<string> onEvent, CancellationToken ct = default)
    {
        var outList = new List<(string, string, string)>();
        var json = JsonSerializer.Serialize(new { mode, message, root });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/agent/run");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var tool = doc.RootElement.TryGetProperty("tool", out var tl) ? tl.GetString() ?? "" : "";
                onEvent(data);
                outList.Add((type, tool, data));
            }
            catch { }
        }
        return outList;
    }

    public async Task<string> GrantAsync(string scope, string command, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { scope, command });
        var res = await _http.PostAsync("/v1/approvals/grant", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("approval_token").GetString() ?? "";
    }

    public async Task<string> ExecAsync(string command, string cwd, string? token, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { command, cwd, timeout_s = 120, approval_token = token });
        var res = await _http.PostAsync("/v1/tools/exec", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> ReadAsync(string path, string root, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { path, offset = 1, limit = 200, root });
        var res = await _http.PostAsync("/v1/tools/read", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> GrepAsync(string query, string root, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { query, include = "**/*", regex = false, root });
        var res = await _http.PostAsync("/v1/tools/grep", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> GlobAsync(string pattern, string root, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { pattern, root });
        var res = await _http.PostAsync("/v1/tools/glob", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
