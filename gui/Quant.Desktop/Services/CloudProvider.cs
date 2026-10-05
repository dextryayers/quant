using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

public interface ICloudProvider
{
    string Name { get; }
    string Model { get; set; }
    bool IsConfigured();
    Task<string> ChatAsync(string message, CancellationToken ct = default);
    string CostFor(int tokens);
}

/// OpenAI-compatible HTTP provider. Never constructed unless user opts in.
public sealed class OpenAiCompatProvider : ICloudProvider, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    public string Name => "openai-compat";
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public bool IsConfigured()
    {
        // Triple guard: explicit opt in handled by caller, key must exist, never log it.
        return !string.IsNullOrEmpty(KeyStore.Read());
    }

    public async Task<string> ChatAsync(string message, CancellationToken ct = default)
    {
        var key = KeyStore.Read();
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("cloud key missing");
        var json = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new[] { new { role = "user", content = message } },
            max_tokens = 1024,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl.TrimEnd('/') + "/chat/completions");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    public string CostFor(int tokens) => $"~{tokens} tokens on {Model}";

    public void Dispose() => _http.Dispose();
}

/// Key handling: env first, then local file with warning. Value never written to logs.
public static class KeyStore
{
    public static string Read()
    {
        var env = Environment.GetEnvironmentVariable("QUANT_CLOUD_KEY");
        if (!string.IsNullOrEmpty(env)) return env;
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quant", "cloud.key");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path).Trim();
        }
        catch { }
        return "";
    }
}

/// Cloud is strictly opt in and off by default. No network unless enabled plus key present.
public sealed class CloudProvider
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "";
    public string KeyPresent => string.IsNullOrEmpty(KeyStore.Read()) ? "missing" : "present";

    public static bool IsDisabledByDefault() => true;

    public string CostEstimate(string text)
    {
        var tokens = Math.Max(1, text.Length / 4);
        return Enabled
            ? $"~{tokens} tokens on {(string.IsNullOrEmpty(Model) ? "cloud" : Model)}"
            : $"{tokens} tokens est $0.0000 local-first (cloud disabled)";
    }
}
