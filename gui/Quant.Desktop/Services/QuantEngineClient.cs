using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

public sealed class QuantEngineClient : IDisposable
{
    private readonly HttpClient _http;

    public QuantEngineClient(string baseUrl = "http://127.0.0.1:3737")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };
    }

    public void Dispose() => _http.Dispose();

    public async Task<string> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync("/health", ct);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            return "unreachable";
        }
    }

    public record ChatMsg(string role, string content);

    // Non-streaming fallback
    public async Task<string> ChatOnceAsync(string userPrompt, string editorContext, CancellationToken ct = default)
    {
        var payload = new
        {
            model = "mock-qwen-coder-7b-q4",
            stream = false,
            messages = new[]
            {
                new { role = "system", content = $"Editor context:\n{editorContext}" },
                new { role = "user", content = userPrompt }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        var res = await _http.PostAsync("/v1/chat/completions",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    // Streaming SSE: POST /v1/chat/stream -> event: data: {"delta":"..."} ... data: [DONE]
    public async Task ChatStreamAsync(
        string userPrompt,
        string editorContext,
        Action<string> onDelta,
        CancellationToken ct = default)
    {
        var payload = new
        {
            model = "mock-qwen-coder-7b-q4",
            stream = true,
            messages = new[]
            {
                new { role = "system", content = $"Editor context:\n{editorContext}" },
                new { role = "user", content = userPrompt }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/stream");
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
            // Axum SSE format: "data: {...}"
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line["data:".Length..].Trim();
                if (data == "[DONE]") break;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("delta", out var d))
                        onDelta(d.GetString() ?? "");
                }
                catch { /* ignore malformed chunk */ }
            }
        }
    }
}
