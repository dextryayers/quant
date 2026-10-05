using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class ModelService : IDisposable
{
    private readonly HttpClient _http;
    public ModelService(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };
    }
    public void Dispose() => _http.Dispose();

    public async Task<List<EngineModel>> ListAsync(CancellationToken ct = default)
    {
        var outList = new List<EngineModel>();
        try
        {
            var res = await _http.GetAsync("/v1/models", ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var e in data.EnumerateArray())
                {
                    outList.Add(new EngineModel
                    {
                        Id = e.GetProperty("id").GetString() ?? "",
                        File = e.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                        SizeMb = e.TryGetProperty("size_mb", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null,
                        Loaded = e.TryGetProperty("loaded", out var l) && l.GetBoolean(),
                        RamMb = e.TryGetProperty("ram_mb", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null,
                    });
                }
            }
            if (doc.RootElement.TryGetProperty("presets", out var presets))
            {
                foreach (var p in presets.EnumerateArray())
                {
                    var id = p.GetProperty("id").GetString() ?? "";
                    if (outList.Exists(m => m.Id == id)) continue;
                    outList.Add(new EngineModel
                    {
                        Id = id,
                        File = p.GetProperty("filename").GetString() ?? "",
                        SizeMb = p.GetProperty("size_mb").GetDouble(),
                        Loaded = false,
                        IsPreset = true,
                    });
                }
            }
        }
        catch { }
        return outList;
    }

    public async Task<string> LoadAsync(string id, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { id, context = 8192 });
        var res = await _http.PostAsync("/v1/models/load", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return body;
    }

    public async Task UnloadAsync(CancellationToken ct = default)
    {
        await _http.PostAsync("/v1/models/unload", new StringContent("{}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
    }

    public async Task<string> StartDownloadAsync(string repo, string file, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { hf_repo = repo, filename = file });
        var res = await _http.PostAsync("/v1/models/download", new StringContent(json, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("job_id").GetString() ?? "";
    }

    public async Task<List<RagHit>> CodeSearchAsync(string baseUrl, string query, int topK = 5)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
            var json = JsonSerializer.Serialize(new { query, top_k = topK });
            var res = await http.PostAsync("/v1/search/code", new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = new List<RagHit>();
            using var doc = JsonDocument.Parse(body);
            foreach (var h in doc.RootElement.GetProperty("hits").EnumerateArray())
            {
                list.Add(new RagHit
                {
                    Path = h.GetProperty("path").GetString() ?? "",
                    Start = h.GetProperty("start_line").GetInt32(),
                    Score = h.GetProperty("score").GetSingle(),
                    Why = h.GetProperty("why").GetString() ?? "",
                });
            }
            return list;
        }
        catch { return new List<RagHit>(); }
    }

    public sealed class RagHit
    {
        public string Path { get; set; } = "";
        public int Start { get; set; }
        public float Score { get; set; }
        public string Why { get; set; } = "";
    }
}
