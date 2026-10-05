using System;
using System.Collections.Generic;
using System.IO;

namespace Quant.Desktop.Services;

public sealed class DiffService
{
    private readonly Stack<(string File, string Backup)> _undo = new();

    public bool TryApplyCreate(string file, string code, out string error)
    {
        error = "";
        try
        {
            Backup(file);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
            File.WriteAllText(file, code);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public bool TryApplyReplace(string file, string oldText, string newText, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(file)) { error = "File not found."; return false; }
            var content = File.ReadAllText(file);
            if (!content.Contains(oldText, StringComparison.Ordinal))
            {
                // Fallback V0.1.0: append as new block so Apply never dead ends.
                Backup(file);
                File.WriteAllText(file, content + "\n" + newText);
                return true;
            }
            Backup(file);
            File.WriteAllText(file, content.Replace(oldText, newText, StringComparison.Ordinal));
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public bool Undo(out string file)
    {
        file = "";
        if (_undo.Count == 0) return false;
        var (f, bak) = _undo.Pop();
        try
        {
            File.Copy(bak, f, true);
            File.Delete(bak);
            file = f;
            return true;
        }
        catch { return false; }
    }

    private void Backup(string file)
    {
        if (!File.Exists(file)) return;
        var bak = file + ".quantdiffbak";
        File.Copy(file, bak, true);
        _undo.Push((file, bak));
        while (_undo.Count > 20)
        {
            var (_, old) = _undo.Pop();
            try { File.Delete(old); } catch { }
        }
    }

    public static List<(string Lang, string Code)> ExtractCodeBlocks(string markdown)
    {
        var list = new List<(string, string)>();
        var lines = markdown.Split('\n');
        string? lang = null;
        var buf = new List<string>();
        foreach (var l in lines)
        {
            if (l.TrimStart().StartsWith("```"))
            {
                if (lang == null) { lang = l.Trim().Trim('`'); buf.Clear(); }
                else { list.Add((lang, string.Join('\n', buf))); lang = null; }
                continue;
            }
            if (lang != null) buf.Add(l);
        }
        return list;
    }
}
