using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Quant.Desktop.Models;

namespace Quant.Desktop.Views;

/// Picks the user or assistant bubble. Keeps the thread calm and scannable.
public sealed class ChatTemplateSelector : IDataTemplate
{
    public IDataTemplate? UserTemplate { get; set; }
    public IDataTemplate? AssistantTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is ChatMessage m && m.IsUser)
            return UserTemplate?.Build(param);
        return AssistantTemplate?.Build(param);
    }

    public bool Match(object? data) => data is ChatMessage;
}
