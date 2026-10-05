using System;
using System.Collections.Generic;

namespace Quant.Desktop.Services;

public enum TermKey
{
    Enter, Backspace, Tab, Escape,
    Up, Down, Left, Right, Home, End, Delete,
    CtrlC, CtrlD,
}

public struct TermCell
{
    public char Ch;
    public uint Fg;
    public uint Bg;
    public bool Bold;
}

/// One rendered line. Cells always span the full width.
public sealed class TermRow
{
    public readonly List<TermCell> Cells = new();
}

/// Shared surface for real terminals (pty) and pipe fallbacks.
/// The view renders Rows + cursor; PlainText feeds attach-to-chat and problems.
public interface ITermSession : IDisposable
{
    string Id { get; }
    string Label { get; }
    string Cwd { get; }
    bool Running { get; }
    event Action? Changed;
    string PlainText { get; }
    IReadOnlyList<TermRow> Rows { get; }
    int CursorRow { get; }
    int CursorCol { get; }
    bool CursorVisible { get; }
    void SendText(string text);
    void SendKey(TermKey key);
    void Resize(int cols, int rows);
    void Kill();
    void Clear();
    void Restart();
}
