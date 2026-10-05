using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Quant.Desktop.Services;

/// Real terminal session.
/// Windows: official conhost.exe in headless mode hosts the shell, so
/// behavior is pixel-identical to the native console: prompts, colors,
/// fullscreen apps, Ctrl+C. We speak plain text in, VT out.
/// Unix: forkpty ($SHELL). Throws when unavailable; caller falls back to pipes.
public sealed class PtySession : ITermSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..6];
    public string Cwd { get; }
    public string ShellExe { get; }
    public string ShellShort { get; }
    public string Label => $"{ShellShort} · {Id}";
    public int ChildPid { get; private set; }

    private readonly VtGrid _grid = new();
    private readonly object _writeGate = new();
    private Process? _proc;
    private StreamWriter? _stdin;
    private volatile bool _dead;

    public event Action? Changed;

    private PtySession(string cwd, string exe, string shortName)
    {
        Cwd = cwd;
        ShellExe = exe;
        ShellShort = shortName;
    }

    public static PtySession Start(string cwd, string? shell, int cols = 120, int rows = 30)
    {
        string exe, shortName;
        if (string.IsNullOrWhiteSpace(shell))
        {
            var all = TerminalSession.AvailableShells();
            exe = all[0].Exe;
            shortName = all[0].Short;
        }
        else
        {
            var all = TerminalSession.AvailableShells();
            exe = shell;
            shortName = shell;
            foreach (var (e, s) in all)
            {
                var file = Path.GetFileName(e);
                if (file.StartsWith(shell, StringComparison.OrdinalIgnoreCase) || s.Equals(shell, StringComparison.OrdinalIgnoreCase))
                {
                    exe = e;
                    shortName = s;
                    break;
                }
            }
        }
        var session = new PtySession(cwd, exe, shortName);
        session._grid.Reset(cols, rows);
        if (OperatingSystem.IsWindows())
            session.StartWindows(cols, rows);
        else
            session.StartUnix(cols, rows);
        return session;
    }

    public bool Running => _proc != null && !_proc.HasExited && !_dead;

    public string PlainText
    {
        get
        {
            lock (_grid)
            {
                var sb = new StringBuilder();
                foreach (var line in _grid.Buffer)
                {
                    var span = new string(line.Cells.ConvertAll(c => c.Ch).ToArray()).TrimEnd();
                    if (span.Length == 0 && sb.Length == 0) continue;
                    sb.AppendLine(span);
                    if (sb.Length > 64000) break;
                }
                var s = sb.ToString();
                return s.Length > 64000 ? s[^64000..] : s;
            }
        }
    }

    public IReadOnlyList<TermRow> Rows
    {
        get
        {
            lock (_grid) return _grid.Buffer;
        }
    }

    public int CursorRow { get { lock (_grid) return _grid.CursorRow; } }
    public int CursorCol { get { lock (_grid) return _grid.CursorCol; } }
    public bool CursorVisible => true;

    private int _sentCols = -1;
    private int _sentRows = -1;

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(20, Math.Min(500, cols));
        rows = Math.Max(5, Math.Min(200, rows));
        lock (_grid) _grid.Resize(cols, rows);
        // Talking to the live console on every drag tick would stutter,
        // so only forward real size changes.
        if (cols == _sentCols && rows == _sentRows)
        {
            Changed?.Invoke();
            return;
        }
        _sentCols = cols;
        _sentRows = rows;
        try
        {
            if (OperatingSystem.IsWindows() && ChildPid > 0)
                ResizeConsole(ChildPid, cols, rows);
            else if (!OperatingSystem.IsWindows())
                ResizeUnix(cols, rows);
        }
        catch { }
        Changed?.Invoke();
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_writeGate)
        {
            try
            {
                if (_proc == null || _proc.HasExited || _stdin == null)
                {
                    MarkDead(-1);
                    return;
                }
                _stdin.Write(text);
                _stdin.Flush();
            }
            catch { MarkDead(-1); }
        }
    }

    public void SendKey(TermKey key)
    {
        var seq = key switch
        {
            TermKey.Enter => "\r",
            TermKey.Backspace => "\x7F",
            TermKey.Tab => "\t",
            TermKey.Escape => "\x1B",
            TermKey.Up => "\x1B[A",
            TermKey.Down => "\x1B[B",
            TermKey.Right => "\x1B[C",
            TermKey.Left => "\x1B[D",
            TermKey.Home => "\x1B[H",
            TermKey.End => "\x1B[F",
            TermKey.Delete => "\x1B[3~",
            TermKey.CtrlC => "\x03",
            TermKey.CtrlD => "\x04",
            _ => "",
        };
        if (seq.Length > 0) SendText(seq);
    }

    public void Kill()
    {
        try
        {
            if (_proc != null && !_proc.HasExited)
                _proc.Kill(true);
        }
        catch { }
        MarkDead(-1);
    }

    public void Clear()
    {
        lock (_grid) _grid.Reset(_grid.Cols, _grid.ViewRows);
        Changed?.Invoke();
    }

    public void Restart()
    {
        Kill();
        lock (_grid) _grid.Reset(_grid.Cols, _grid.ViewRows);
        try
        {
            if (OperatingSystem.IsWindows())
                StartWindows(_grid.Cols, _grid.ViewRows);
            else
                StartUnix(_grid.Cols, _grid.ViewRows);
        }
        catch { }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Kill();
        try { _stdin?.Dispose(); } catch { }
        try { _proc?.Dispose(); } catch { }
    }

    private void MarkDead(int code)
    {
        _dead = true;
        Changed?.Invoke();
    }

    private void OnBytes(byte[] data, int count)
    {
        lock (_grid) _grid.Put(data, count);
        Changed?.Invoke();
    }

    // ---------------- Windows: headless conhost ----------------

    private void StartWindows(int cols, int rows)
    {
        var conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        if (!File.Exists(conhost))
            throw new InvalidOperationException("conhost.exe missing");
        var shellArgs = ShellShort is "PS7" or "PS" ? " -NoLogo" : "";
        var psi = new ProcessStartInfo
        {
            FileName = conhost,
            Arguments = $"--headless --width {Math.Max(20, cols)} --height {Math.Max(5, rows)} -- \"{ShellExe}\"{shellArgs}",
            WorkingDirectory = Cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, __) =>
        {
            try { MarkDead(proc.ExitCode); } catch { MarkDead(-1); }
        };
        if (!proc.Start())
            throw new InvalidOperationException("conhost failed to start");
        _proc = proc;
        _stdin = proc.StandardInput;
        _stdin.AutoFlush = true;
        try { ChildPid = proc.Id; } catch { }
        // Raw byte reads: prompts without newlines (>>> , password:) stream too.
        var stdout = proc.StandardOutput.BaseStream;
        var stderr = proc.StandardError.BaseStream;
        _reader = new Thread(() =>
        {
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    var n = stdout.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    OnBytes(buf, n);
                }
            }
            catch { }
            MarkDead(-1);
        })
        { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
        var errThread = new Thread(() =>
        {
            var buf = new byte[4096];
            try
            {
                while (true)
                {
                    var n = stderr.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    OnBytes(buf, n);
                }
            }
            catch { }
        })
        { IsBackground = true, Name = "pty-err" };
        errThread.Start();
    }

    /// Resize a live headless console by briefly attaching to it.
    private static void ResizeConsole(int pid, int cols, int rows)
    {
        cols = Math.Max(20, Math.Min(500, cols));
        rows = Math.Max(5, Math.Min(200, rows));
        if (!Native.AttachConsole(pid))
            return;
        try
        {
            var hOut = Native.GetStdHandle(-11);
            if (hOut == IntPtr.Zero || hOut == new IntPtr(-1))
                return;
            // Shrink the window first so the buffer can change freely.
            var tiny = new Native.SMALL_RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };
            Native.SetConsoleWindowInfo(hOut, true, ref tiny);
            var size = new Native.COORD { X = (short)cols, Y = 9000 };
            Native.SetConsoleScreenBufferSize(hOut, size);
            var win = new Native.SMALL_RECT { Left = 0, Top = 0, Right = (short)(cols - 1), Bottom = (short)(rows - 1) };
            Native.SetConsoleWindowInfo(hOut, true, ref win);
        }
        finally
        {
            Native.FreeConsole();
        }
    }

    // ---------------- Unix forkpty ----------------

    private int _masterFd = -1;
    private FileStream? _masterStream;
    private Thread? _reader;

    private void StartUnix(int cols, int rows)
    {
        var ws = new Native.Winsize { ws_col = (ushort)Math.Max(20, cols), ws_row = (ushort)Math.Max(5, rows) };
        int master;
        int pid;
        try
        {
            pid = Native.ForkPty(out master, null, IntPtr.Zero, ref ws);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("no pty support: " + ex.Message);
        }
        if (pid < 0)
            throw new InvalidOperationException("forkpty failed");
        if (pid == 0)
        {
            // Child: become the shell.
            try
            {
                Environment.SetEnvironmentVariable("TERM", "xterm-256color");
                Native.ExecVp(ShellExe, new[] { ShellExe });
            }
            catch { }
            Native.Exit(127);
            return;
        }
        _masterFd = master;
        try { ChildPid = pid; } catch { }
        // SafeFileHandle releases via close() on Unix, CloseHandle on Windows.
        _masterStream = new FileStream(new SafeFileHandle(new IntPtr(master), true), FileAccess.ReadWrite, 4096, false);
        _reader = new Thread(() =>
        {
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    var n = _masterStream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    OnBytes(buf, n);
                }
            }
            catch { }
            MarkDead(-1);
        })
        { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
    }

    private void ResizeUnix(int cols, int rows)
    {
        if (_masterFd < 0) return;
        var ws = new Native.Winsize
        {
            ws_col = (ushort)Math.Max(20, cols),
            ws_row = (ushort)Math.Max(5, rows),
        };
        if (Native.Ioctl(_masterFd, Native.TIOCSWINSZ_LINUX, ref ws) != 0)
            Native.Ioctl(_masterFd, Native.TIOCSWINSZ_MAC, ref ws);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Winsize
        {
            public ushort ws_row;
            public ushort ws_col;
            public ushort ws_xpixel;
            public ushort ws_ypixel;
        }

        public const ulong TIOCSWINSZ_LINUX = 0x5414;
        public const ulong TIOCSWINSZ_MAC = 0x80087467;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleScreenBufferSize(IntPtr hConsoleOutput, COORD dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleWindowInfo(IntPtr hConsoleOutput, [MarshalAs(UnmanagedType.Bool)] bool bAbsolute, ref SMALL_RECT lpConsoleWindow);

        [DllImport("libutil", EntryPoint = "forkpty", SetLastError = true)]
        public static extern int ForkPty(out int amaster, string? name, IntPtr termios, ref Winsize winsize);

        [DllImport("libc", EntryPoint = "execvp", SetLastError = true)]
        public static extern int ExecVp(string file, string[] argv);

        [DllImport("libc", EntryPoint = "_exit")]
        public static extern void Exit(int status);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        public static extern int Ioctl(int fd, ulong request, ref Winsize ws);
    }
}
