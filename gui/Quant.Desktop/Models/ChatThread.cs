using System;
using System.Collections.Generic;

namespace Quant.Desktop.Models;

public sealed class ChatThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New thread";
    public string Mode { get; set; } = "Edit";
    public DateTime Updated { get; set; } = DateTime.Now;
    public List<ChatMessage> Messages { get; set; } = new();
}
