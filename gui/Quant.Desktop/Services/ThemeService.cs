using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Quant.Desktop.Services;

public sealed class ThemeService
{
    public string Current { get; private set; } = "dark-premium";

    private static readonly Dictionary<string, Dictionary<string, string>> Themes = new()
    {
        ["dark-premium"] = new()
        {
            ["QuantBgBase"] = "#0F1115",
            ["QuantSurface1"] = "#151923",
            ["QuantSurface2"] = "#1C2230",
            ["QuantBorderSubtle"] = "#2A3346",
            ["QuantTextPrimary"] = "#E8ECF3",
            ["QuantTextSecondary"] = "#9AA4B8",
            ["QuantTextMuted"] = "#6B7690",
            ["QuantAccent"] = "#6E9DFF",
            ["QuantChatAssistant"] = "#151923",
            ["QuantEditorBg"] = "#0F1115",
        },
        ["light-pro"] = new()
        {
            ["QuantBgBase"] = "#F7F8FA",
            ["QuantSurface1"] = "#FFFFFF",
            ["QuantSurface2"] = "#EEF1F6",
            ["QuantBorderSubtle"] = "#D9E0EC",
            ["QuantTextPrimary"] = "#141A26",
            ["QuantTextSecondary"] = "#5A657A",
            ["QuantTextMuted"] = "#8A94A8",
            ["QuantAccent"] = "#2F6BFF",
            ["QuantChatAssistant"] = "#FFFFFF",
            ["QuantEditorBg"] = "#FFFFFF",
        }
    };

    public void Apply(string name)
    {
        if (!Themes.TryGetValue(name, out var tokens)) return;
        Current = name;
        var res = Application.Current?.Resources;
        if (res == null) return;
        foreach (var (k, hex) in tokens)
        {
            if (res.ContainsKey(k) && res[k] is SolidColorBrush)
                res[k] = new SolidColorBrush(Color.Parse(hex));
            else
                res[k] = new SolidColorBrush(Color.Parse(hex));
        }
    }

    public void Toggle() => Apply(Current == "dark-premium" ? "light-pro" : "dark-premium");
}
