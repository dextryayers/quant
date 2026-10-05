using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quant.Desktop.Services;

public sealed class CompleteService : IDisposable
{
    private readonly HttpClient _http;
    private CancellationTokenSource? _debounce;
    private string _lastKey = "";
    private readonly List<string> _cache = new();

    public CompleteService(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    }
    public void Dispose() { _debounce?.Cancel(); _http.Dispose(); }

    public sealed class Suggestion
    {
        public string Text { get; set; } = "";
        public float Score { get; set; }
        public string Why { get; set; } = "";
    }

    public async Task<List<Suggestion>> FetchAsync(string prefix, string suffix, string file, List<string> hints, CancellationToken ct = default)
    {
        // 250 ms debounce
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _debounce.Token);
        try { await Task.Delay(250, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new List<Suggestion>(); }

        var key = $"{file}:{prefix.Length}:{suffix.Length}";
        try
        {
            var json = JsonSerializer.Serialize(new { prefix, suffix, file, top_k = 3, hints });
            var res = await _http.PostAsync("/v1/complete", new StringContent(json, Encoding.UTF8, "application/json"), linked.Token).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            var list = new List<Suggestion>();
            using var doc = JsonDocument.Parse(body);
            foreach (var s in doc.RootElement.GetProperty("suggestions").EnumerateArray())
            {
                list.Add(new Suggestion
                {
                    Text = s.GetProperty("text").GetString() ?? "",
                    Score = s.GetProperty("score").GetSingle(),
                    Why = s.GetProperty("why").GetString() ?? "",
                });
            }
            _lastKey = key;
            return list;
        }
        catch { return new List<Suggestion>(); }
    }
}
