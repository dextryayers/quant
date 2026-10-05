using System;
using System.Collections.Generic;
using System.Text;

namespace Quant.Desktop.Services;

/// Minimal VT100/xterm grid: printable text, cursor, erase, scroll regions,
/// SGR colors (16/256/RGB), OSC title skip. Enough for cmd, powershell,
/// git, npm, dotnet, cargo, python with zero garble.
public sealed class VtGrid
{
    public int Cols { get; private set; } = 80;
    public int ViewRows { get; private set; } = 24;

    private readonly List<TermRow> _scrollback = new();
    private readonly List<TermRow> _screen = new();
    private int _cx;
    private int _cy;
    private uint _fg = DefaultFg;
    private uint _bg;
    private bool _bold;
    private int _savedX;
    private int _savedY;
    private int _marginTop;
    private int _marginBottom = -1; // -1 means bottom of screen

    private const int MaxScrollback = 2000;
    private const uint DefaultFg = 0xFFA7A7B3;

    // Parser state
    private int _state; // 0 normal, 1 esc, 2 csi, 3 osc
    private readonly StringBuilder _params = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuf = new char[256];

    public VtGrid()
    {
        Reset(80, 24);
    }

    public void Reset(int cols, int rows)
    {
        Cols = Math.Max(20, cols);
        ViewRows = Math.Max(5, rows);
        _scrollback.Clear();
        _screen.Clear();
        for (var i = 0; i < ViewRows; i++) _screen.Add(BlankLine());
        _cx = 0;
        _cy = 0;
        _fg = DefaultFg;
        _bg = 0;
        _bold = false;
        _marginTop = 0;
        _marginBottom = -1;
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(20, cols);
        rows = Math.Max(5, rows);
        if (cols == Cols && rows == ViewRows) return;
        Cols = cols;
        ViewRows = rows;
        foreach (var l in _scrollback) FitLine(l);
        foreach (var l in _screen) FitLine(l);
        while (_screen.Count > ViewRows) { PushScroll(_screen[0]); _screen.RemoveAt(0); }
        while (_screen.Count < ViewRows) _screen.Add(BlankLine());
        _cx = Math.Min(_cx, Cols - 1);
        _cy = Math.Min(_cy, ViewRows - 1);
        _marginTop = 0;
        _marginBottom = -1;
    }

    private void FitLine(TermRow l)
    {
        while (l.Cells.Count < Cols) l.Cells.Add(new TermCell { Ch = ' ' });
        if (l.Cells.Count > Cols) l.Cells.RemoveRange(Cols, l.Cells.Count - Cols);
    }

    private TermRow BlankLine()
    {
        var l = new TermRow();
        for (var i = 0; i < Cols; i++) l.Cells.Add(new TermCell { Ch = ' ' });
        return l;
    }

    private void PushScroll(TermRow l)
    {
        _scrollback.Add(l);
        if (_scrollback.Count > MaxScrollback)
            _scrollback.RemoveRange(0, _scrollback.Count - MaxScrollback);
    }

    private int BottomMargin => _marginBottom < 0 ? ViewRows - 1 : Math.Min(_marginBottom, ViewRows - 1);

    /// All lines, scrollback first, screen last. CursorRow is absolute here.
    public IReadOnlyList<TermRow> Buffer
    {
        get
        {
            var all = new List<TermRow>(_scrollback.Count + _screen.Count);
            all.AddRange(_scrollback);
            all.AddRange(_screen);
            return all;
        }
    }

    public int CursorRow => _scrollback.Count + _cy;
    public int CursorCol => _cx;

    public void Put(byte[] data, int count)
    {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(count) + 1];
        _decoder.Convert(data, 0, count, chars, 0, chars.Length, false, out _, out var completed, out _);
        for (var i = 0; i < completed; i++) PutChar(chars[i]);
    }

    private void PutChar(char c)
    {
        switch (_state)
        {
            case 0:
                if (c == '\x1B') { _state = 1; return; }
                if (c == '\n') { NewLine(); return; }
                if (c == '\r') { _cx = 0; return; }
                if (c == '\b') { if (_cx > 0) _cx--; return; }
                if (c == '\t') { _cx = Math.Min(Cols - 1, ((_cx / 8) + 1) * 8); return; }
                if (c == '\a') return;
                if (char.IsControl(c)) return;
                EnsureCursor();
                _screen[_cy].Cells[_cx] = new TermCell { Ch = c, Fg = _fg, Bg = _bg, Bold = _bold };
                _cx++;
                if (_cx >= Cols) { _cx = 0; NewLine(); }
                return;
            case 1: // ESC
                if (c == '[') { _state = 2; _params.Clear(); return; }
                if (c == ']') { _state = 3; _params.Clear(); return; }
                _state = 0;
                if (c == '7') { _savedX = _cx; _savedY = _cy; }
                else if (c == '8') { _cx = _savedX; _cy = _savedY; ClampCursor(); }
                else if (c == 'M') { if (_cy > _marginTop) _cy--; }
                else if (c == 'c') Reset(Cols, ViewRows);
                return;
            case 2: // CSI
                if ((c >= '0' && c <= '9') || c == ';' || c == '?' || c == '>' || c == '!' || c == '"' || c == '$' || c == '\'')
                {
                    if (_params.Length < 64) _params.Append(c);
                    return;
                }
                _state = 0;
                HandleCsi(c, _params.ToString());
                return;
            default: // OSC, ends on BEL or ESC backslash
                if (c == '\a') _state = 0;
                else if (c == '\x1B') _state = 1;
                return;
        }
    }

    private void EnsureCursor()
    {
        _cx = Math.Max(0, Math.Min(Cols - 1, _cx));
        _cy = Math.Max(0, Math.Min(ViewRows - 1, _cy));
    }

    private void ClampCursor() => EnsureCursor();

    private void NewLine()
    {
        if (_cy == BottomMargin)
        {
            if (_marginTop == 0 && (_marginBottom < 0 || _marginBottom >= ViewRows - 1))
                PushScroll(_screen[0]);
            for (var y = _marginTop; y < BottomMargin; y++)
                _screen[y] = _screen[y + 1];
            _screen[BottomMargin] = BlankLine();
        }
        else if (_cy < ViewRows - 1)
        {
            _cy++;
        }
        _cx = 0;
    }

    private void HandleCsi(char final, string raw)
    {
        var privFlag = raw.StartsWith("?");
        var body = privFlag ? raw[1..] : raw;
        var parts = body.Split(';', StringSplitOptions.None);
        int P(int i, int def)
        {
            if (i < parts.Length && int.TryParse(parts[i], out var v) && v > 0) return v;
            return def;
        }
        switch (final)
        {
            case 'A': _cy = Math.Max(_marginTop, _cy - P(0, 1)); break;
            case 'B': _cy = Math.Min(BottomMargin, _cy + P(0, 1)); break;
            case 'C': _cx = Math.Min(Cols - 1, _cx + P(0, 1)); break;
            case 'D': _cx = Math.Max(0, _cx - P(0, 1)); break;
            case 'E': _cy = Math.Min(BottomMargin, _cy + P(0, 1)); _cx = 0; break;
            case 'F': _cy = Math.Max(_marginTop, _cy - P(0, 1)); _cx = 0; break;
            case 'G': _cx = Math.Max(0, Math.Min(Cols - 1, P(0, 1) - 1)); break;
            case 'H':
            case 'f':
                _cy = Math.Max(0, Math.Min(ViewRows - 1, P(0, 1) - 1));
                _cx = Math.Max(0, Math.Min(Cols - 1, P(1, 1) - 1));
                break;
            case 'd': _cy = Math.Max(0, Math.Min(ViewRows - 1, P(0, 1) - 1)); break;
            case 'J':
                {
                    var n = P(0, 0);
                    if (n == 2 || n == 3)
                    {
                        for (var y = 0; y < ViewRows; y++) _screen[y] = BlankLine();
                        _cx = 0; _cy = 0;
                    }
                    else if (n == 0)
                    {
                        for (var x = _cx; x < Cols; x++) _screen[_cy].Cells[x] = Erased();
                        for (var y = _cy + 1; y < ViewRows; y++) _screen[y] = BlankLine();
                    }
                    else if (n == 1)
                    {
                        for (var y = 0; y < _cy; y++) _screen[y] = BlankLine();
                        for (var x = 0; x <= _cx; x++) _screen[_cy].Cells[x] = Erased();
                    }
                    break;
                }
            case 'K':
                {
                    var n = P(0, 0);
                    if (n == 2) _screen[_cy] = BlankLine();
                    else if (n == 1) { for (var x = 0; x <= _cx; x++) _screen[_cy].Cells[x] = Erased(); }
                    else { for (var x = _cx; x < Cols; x++) _screen[_cy].Cells[x] = Erased(); }
                    break;
                }
            case 'm': ApplySgr(parts); break;
            case 'r':
                {
                    var top = P(0, 1) - 1;
                    var bottom = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b - 1 : ViewRows - 1;
                    _marginTop = Math.Max(0, top);
                    _marginBottom = Math.Max(_marginTop, Math.Min(ViewRows - 1, bottom));
                    _cx = 0; _cy = _marginTop;
                    break;
                }
            case 's': _savedX = _cx; _savedY = _cy; break;
            case 'u': _cx = _savedX; _cy = _savedY; ClampCursor(); break;
            case 'h':
            case 'l':
                // Private modes (alt screen 1049/1047/47, cursor 25): approximate.
                if (privFlag && (body.Contains("1049") || body.Contains("1047") || body == "47"))
                {
                    if (final == 'h')
                    {
                        for (var y = 0; y < ViewRows; y++) _screen[y] = BlankLine();
                        _cx = 0; _cy = 0;
                    }
                    _marginTop = 0;
                    _marginBottom = -1;
                }
                break;
        }
    }

    private TermCell Erased() => new() { Ch = ' ', Fg = _fg, Bg = _bg };

    private void ApplySgr(string[] parts)
    {
        if (parts.Length == 1 && (parts[0] == "" || parts[0] == "0"))
        {
            _fg = DefaultFg; _bg = 0; _bold = false;
            return;
        }
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var n)) continue;
            if (n == 0) { _fg = DefaultFg; _bg = 0; _bold = false; }
            else if (n == 1) _bold = true;
            else if (n == 22) _bold = false;
            else if (n >= 30 && n <= 37) _fg = Ansi16(n - 30, false);
            else if (n >= 90 && n <= 97) _fg = Ansi16(n - 90, true);
            else if (n == 39) _fg = DefaultFg;
            else if (n >= 40 && n <= 47) _bg = Ansi16(n - 40, false);
            else if (n >= 100 && n <= 107) _bg = Ansi16(n - 100, true);
            else if (n == 49) _bg = 0;
            else if ((n == 38 || n == 48) && i + 1 < parts.Length)
            {
                var isFg = n == 38;
                if (parts[i + 1] == "5" && i + 2 < parts.Length && int.TryParse(parts[i + 2], out var c256))
                {
                    if (isFg) _fg = Ansi256(c256); else _bg = Ansi256(c256);
                    i += 2;
                }
                else if (parts[i + 1] == "2" && i + 4 < parts.Length
                    && int.TryParse(parts[i + 2], out var r) && int.TryParse(parts[i + 3], out var g) && int.TryParse(parts[i + 4], out var b))
                {
                    var col = (uint)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b);
                    if (isFg) _fg = col; else _bg = col;
                    i += 4;
                }
            }
        }
    }

    private static uint Ansi16(int idx, bool bright)
    {
        uint[] normal = { 0xFF3A3A44, 0xFFE5484D, 0xFF43C488, 0xFFD9A648, 0xFF8FA2FF, 0xFFC792EA, 0xFF56B6C2, 0xFFC5C5CE };
        uint[] brights = { 0xFF6E6E78, 0xFFF07178, 0xFF66D9A0, 0xFFE5C07B, 0xFFA3B2FF, 0xFFD7A1F9, 0xFF7AD9E8, 0xFFFFFFFF };
        return (bright ? brights : normal)[Math.Max(0, Math.Min(7, idx))];
    }

    private static uint Ansi256(int c)
    {
        if (c < 0) return DefaultFg;
        if (c < 8) return Ansi16(c, false);
        if (c < 16) return Ansi16(c - 8, true);
        if (c < 232)
        {
            c -= 16;
            int[] levels = { 0, 95, 135, 175, 215, 255 };
            uint r = (uint)levels[c / 36];
            uint g = (uint)levels[(c / 6) % 6];
            uint b = (uint)levels[c % 6];
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        uint gray = (uint)(8 + (c - 232) * 10);
        return 0xFF000000 | (gray << 16) | (gray << 8) | gray;
    }
}
