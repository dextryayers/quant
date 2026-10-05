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
            ["QuantTextPrimary"] = "#EFEFF2",
            ["QuantTextSecondary"] = "#A7A7B3",
            ["QuantTextMuted"] = "#71717C",
            ["QuantAccent"] = "#8FA2FF",
            ["QuantAccentSoft"] = "#262C4E",
            ["QuantChatUser"] = "#232A45",
            ["QuantChatAssistant"] = "#17171D",
            ["QuantEditorBg"] = "#101014",
        },
        ["light-pro"] = new()
        {
            ["QuantBgBase"] = "#F5F5F7",
            ["QuantSurface1"] = "#FFFFFF",
            ["QuantSurface2"] = "#ECECF0",
            ["QuantOverlay"] = "#FFFFFF",
            ["QuantBorderSubtle"] = "#DCDCE2",
            ["QuantTextPrimary"] = "#17171C",
            ["QuantTextSecondary"] = "#5B5B66",
            ["QuantTextMuted"] = "#8E8E99",
            ["QuantAccent"] = "#4F63E7",
            ["QuantAccentSoft"] = "#E2E6FF",
            ["QuantChatUser"] = "#E9EDFF",
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
