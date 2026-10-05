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
            ["QuantBgBase"] = "#101014",
            ["QuantSurface1"] = "#17171D",
            ["QuantSurface2"] = "#1F1F27",
            ["QuantOverlay"] = "#26262F",
            ["QuantBorderSubtle"] = "#2B2B34",
            ["QuantBorderStrong"] = "#3D3D49",
            ["QuantTextPrimary"] = "#EFEFF2",
            ["QuantTextSecondary"] = "#A7A7B3",
            ["QuantTextMuted"] = "#71717C",
            ["QuantAccent"] = "#8FA2FF",
            ["QuantAccentHover"] = "#A3B2FF",
            ["QuantAccentSoft"] = "#262C4E",
            ["QuantOnAccent"] = "#101014",
            ["QuantSuccess"] = "#43C488",
            ["QuantWarning"] = "#D9A648",
            ["QuantDanger"] = "#E5484D",
            ["QuantChatUser"] = "#232A45",
            ["QuantChatAssistant"] = "#17171D",
            ["QuantEditorBg"] = "#101014",
        },
        ["light-pro"] = new()
        {
            ["QuantBgBase"] = "#F3F3F6",
            ["QuantSurface1"] = "#FFFFFF",
            ["QuantSurface2"] = "#E9E9EF",
            ["QuantOverlay"] = "#FFFFFF",
            ["QuantBorderSubtle"] = "#DBDBE1",
            ["QuantBorderStrong"] = "#BFBFC9",
            ["QuantTextPrimary"] = "#16161B",
            ["QuantTextSecondary"] = "#55555F",
            ["QuantTextMuted"] = "#6F6F79",
            ["QuantAccent"] = "#4F63E7",
            ["QuantAccentHover"] = "#3D52D6",
            ["QuantAccentSoft"] = "#E3E7FF",
            ["QuantOnAccent"] = "#FFFFFF",
            ["QuantSuccess"] = "#1E9E64",
            ["QuantWarning"] = "#A86A12",
            ["QuantDanger"] = "#D92D20",
            ["QuantChatUser"] = "#E6E9FF",
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
