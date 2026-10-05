using System.Text.Json.Serialization;

namespace Quant.Desktop.Models;

public sealed class QuantConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("engine")]
    public EngineSection Engine { get; set; } = new();

    [JsonPropertyName("models")]
    public ModelsSection Models { get; set; } = new();

    [JsonPropertyName("ui")]
    public UiSection Ui { get; set; } = new();

    public sealed class EngineSection
    {
        [JsonPropertyName("port")]
        public int Port { get; set; } = 3737;

        [JsonPropertyName("host")]
        public string Host { get; set; } = "127.0.0.1";
    }

    public sealed class ModelsSection
    {
        [JsonPropertyName("active")]
        public string Active { get; set; } = "qwen2.5-coder-7b-q4_k_m";

        [JsonPropertyName("context")]
        public int Context { get; set; } = 8192;
    }

    public sealed class UiSection
    {
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark-premium";

        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 14;
    }
}
