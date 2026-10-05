using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Quant.Desktop.Services;

namespace Quant.Desktop.Views;

/// Grid terminal surface. Renders the session buffer cell by cell,
/// routes typing straight to the pty, wheel scrolls history.
/// No input box: this IS the terminal.
public sealed class TerminalView : Control
{
    public static readonly StyledProperty<ITermSession?> SessionProperty =
        AvaloniaProperty.Register<TerminalView, ITermSession?>(nameof(Session));

    public ITermSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private Typeface _regular = new(new FontFamily("Cascadia Code"));
    private Typeface _bold = new(new FontFamily("Cascadia Code"), FontStyle.Normal, FontWeight.Bold);
    private double _cellW = 8;
    private double _cellH = 17;
    private bool _measured;
    private int _viewTop;
    private bool _follow = true;
    private ITermSession? _hooked;

    public TerminalView()
    {
        Focusable = true;
        IsTabStop = false;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(Session);
        MeasureCells();
        UpdateViewport();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unhook();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
        {
            Unhook();
            Hook(Session);
            _follow = true;
            _viewTop = 0;
            UpdateViewport();
            InvalidateVisual();
            Focus();
        }
    }

    private void Hook(ITermSession? s)
    {
        if (s == null || ReferenceEquals(s, _hooked)) return;
        _hooked = s;
        s.Changed += OnSessionChanged;
    }

    private void Unhook()
    {
        if (_hooked != null)
        {
            _hooked.Changed -= OnSessionChanged;
            _hooked = null;
        }
    }

    private void OnSessionChanged()
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var s = Session;
        if (s == null) return;
        var total = s.Rows.Count;
        var view = ViewportRows();
        if (total <= view) return;
        if (e.Delta.Y > 0)
        {
            _follow = false;
            _viewTop = Math.Max(0, _viewTop - 3);
        }
        else
        {
            _viewTop = Math.Min(total - view, _viewTop + 3);
            if (_viewTop >= total - view) _follow = true;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            Session?.SendText(e.Text);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var s = Session;
        if (s == null)
        {
            base.OnKeyDown(e);
            return;
        }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        TermKey? key = e.Key switch
        {
            Key.Enter => TermKey.Enter,
            Key.Back => TermKey.Backspace,
            Key.Tab => TermKey.Tab,
            Key.Escape => TermKey.Escape,
            Key.Up => TermKey.Up,
            Key.Down => TermKey.Down,
            Key.Left => TermKey.Left,
            Key.Right => TermKey.Right,
            Key.Home => TermKey.Home,
            Key.End => TermKey.End,
            Key.Delete => TermKey.Delete,
            _ => null,
        };
        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.C) { s.SendKey(TermKey.CtrlC); e.Handled = true; return; }
            if (e.Key == Key.D) { s.SendKey(TermKey.CtrlD); e.Handled = true; return; }
            if (e.Key == Key.L) { s.SendText("\x0C"); e.Handled = true; return; }
            if (e.Key == Key.U) { s.SendText("\x15"); e.Handled = true; return; }
        }
        if (key != null)
        {
            // Terminal owns these keys while focused (like every real terminal).
            if (e.Key == Key.Tab) e.Handled = true;
            else e.Handled = true;
            s.SendKey(key.Value);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateViewport();
    }

    private void MeasureCells()
    {
        try
        {
            var ft = new FormattedText("MMMMMMMMMM", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                _regular, 13, Brushes.White);
            if (ft.Width > 0) _cellW = ft.Width / 10;
            if (ft.Height > 0) _cellH = ft.Height;
            _measured = true;
        }
        catch { }
    }

    private int ViewportCols() => Math.Max(20, (int)(Bounds.Width / Math.Max(1, _cellW)));

    private int ViewportRows() => Math.Max(5, (int)(Bounds.Height / Math.Max(1, _cellH)));

    private void UpdateViewport()
    {
        var s = Session;
        if (s == null || Bounds.Width <= 0) return;
        if (!_measured) MeasureCells();
        s.Resize(ViewportCols(), ViewportRows());
    }

    private SolidColorBrush Brush(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out var b))
        {
            b = new SolidColorBrush(Color.FromUInt32(argb));
            _brushes[argb] = b;
        }
        return b;
    }

    private IBrush BgBrush()
    {
        if (Application.Current?.Resources.TryGetResource("QuantEditorBg", null, out var v) == true && v is SolidColorBrush b)
            return b;
        return Brushes.Black;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(BgBrush(), rect);

        var s = Session;
        if (s == null) return;
        if (!_measured) MeasureCells();

        var rows = s.Rows;
        var total = rows.Count;
        var view = ViewportRows();
        int top;
        if (_follow || total <= view)
        {
            top = Math.Max(0, total - view);
            _viewTop = top;
        }
        else
        {
            top = Math.Max(0, Math.Min(_viewTop, Math.Max(0, total - view)));
        }

        var cursorAbs = s.CursorRow;
        var showCursor = s.CursorVisible && s.Running;

        for (var r = 0; r < view; r++)
        {
            var idx = top + r;
            if (idx < 0 || idx >= total) continue;
            var line = rows[idx];
            var y = r * _cellH;
            var x = 0.0;
            var i = 0;
            while (i < line.Cells.Count)
            {
                var c = line.Cells[i];
                var j = i + 1;
                while (j < line.Cells.Count && line.Cells[j].Fg == c.Fg && line.Cells[j].Bg == c.Bg && line.Cells[j].Bold == c.Bold)
                    j++;
                var chars = new char[j - i];
                for (var k = i; k < j; k++) chars[k - i] = line.Cells[k].Ch;
                var text = new string(chars);
                var w = (j - i) * _cellW;
                if (c.Bg != 0)
                    context.FillRectangle(Brush(c.Bg), new Rect(x, y, w, _cellH));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        c.Bold ? _bold : _regular, 13, Brush(c.Fg));
                    context.DrawText(ft, new Point(x, y));
                }
                x += w;
                i = j;
            }
            if (showCursor && idx == cursorAbs)
            {
                var cx = Math.Max(0, Math.Min(s.CursorCol, line.Cells.Count - 1)) * _cellW;
                var cursorBrush = Brush(0xFF8FA2FF);
                context.FillRectangle(cursorBrush, new Rect(cx, y, _cellW, _cellH));
            }
        }
    }
}
