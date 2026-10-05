using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class ExplorerService
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2" };

    private static readonly string[] DefaultExcludeDirs = { ".git", "node_modules", "target", "bin", "obj", "dist", "build", ".quant" };
    private static readonly string[] DefaultExcludeFiles = { ".DS_Store", "thumbs.db" };

    public string? LastError { get; private set; }
    public bool LargeMode { get; private set; }
    public int LastCount { get; private set; }

    // Clipboard for cut/copy inside workspace
    public List<string> ClipboardPaths { get; } = new();
    public bool ClipboardIsCut { get; private set; }
    public void SetClipboard(IEnumerable<string> paths, bool isCut)
    {
        ClipboardPaths.Clear();
        ClipboardPaths.AddRange(paths);
        ClipboardIsCut = isCut;
    }

    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name cannot be empty.";
        if (name.Length > 200) return "Name is too long.";
        if (name.IndexOfAny(InvalidChars) >= 0) return "Name contains illegal characters.";
        if (name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal))
            return "Name cannot end with space or dot on Windows.";
        var stem = name.Split('.')[0];
        if (Reserved.Contains(stem)) return "Name is reserved on Windows.";
        return null;
    }

    public List<FileNode> BuildRoots(IEnumerable<WorkspaceRoot> roots, string filter, string sort, bool showExcluded)
    {
        var result = new List<FileNode>();
        foreach (var r in roots)
        {
            if (!Directory.Exists(r.Path)) continue;
            var node = new FileNode
            {
                FullPath = r.Path,
                Name = string.IsNullOrWhiteSpace(r.Name) ? new DirectoryInfo(r.Path).Name : r.Name,
                IsDirectory = true,
                Icon = "D",
                IsExpanded = true,
            };
            LoadChildren(node, filter, sort, showExcluded, depth: 0);
            result.Add(node);
        }
        return result;
    }

    public void LoadChildren(FileNode node, string filter, string sort, bool showExcluded, int depth)
    {
        node.Children.Clear();
        if (!node.IsDirectory || !Directory.Exists(node.FullPath)) return;
        try
        {
            var entries = Directory.GetFileSystemEntries(node.FullPath);
            LastCount = entries.Length;
            // Large repo guard: cap initial render, rest via filter/search
            var take = entries.Length > 5000 ? 2000 : entries.Length;
            LargeMode = entries.Length > 100000;
            var dirs = new List<FileNode>();
            var files = new List<FileNode>();
            foreach (var e in entries.Take(take))
            {
                var isDir = Directory.Exists(e);
                var name = Path.GetFileName(e);
                if (!showExcluded && IsExcluded(e, isDir)) continue;
                if (!string.IsNullOrWhiteSpace(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var fn = new FileNode
                {
                    FullPath = e,
                    Name = name,
                    IsDirectory = isDir,
                    Icon = isDir ? "D" : IconFor(name),
                };
                try
                {
                    if (!isDir)
                    {
                        var fi = new FileInfo(e);
                        fn.Size = fi.Length;
                        fn.Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                }
                catch { }
                if (isDir) dirs.Add(fn); else files.Add(fn);
            }
            IEnumerable<FileNode> orderedDirs = sort switch
            {
                "Type" => dirs.OrderBy(d => d.Name),
                "Modified" => dirs.OrderByDescending(d => d.FullPath),
                _ => dirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            };
            IEnumerable<FileNode> orderedFiles = sort switch
            {
                "Type" => files.OrderBy(f => Path.GetExtension(f.Name)).ThenBy(f => f.Name),
                "Modified" => files.OrderByDescending(f => f.Modified),
                _ => files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
            };
            foreach (var d in orderedDirs) node.Children.Add(d);
            foreach (var f in orderedFiles) node.Children.Add(f);
            node.ChildrenLoaded = true;
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public static bool IsExcluded(string path, bool isDir)
    {
        var name = Path.GetFileName(path);
        if (isDir && DefaultExcludeDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        if (!isDir && DefaultExcludeFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string IconFor(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".rs" => "R",
            ".cs" => "C",
            ".ts" or ".tsx" or ".js" => "T",
            ".py" => "P",
            ".md" => "M",
            ".json" or ".toml" or ".yaml" or ".yml" => "J",
            _ => "F",
        };
    }

    public bool CreateFile(string parentDir, string name, out string fullPath)
    {
        fullPath = "";
        var err = ValidateName(name);
        if (err != null) { LastError = err; return false; }
        try
        {
            fullPath = Path.Combine(parentDir, name);
            if (File.Exists(fullPath) || Directory.Exists(fullPath)) { LastError = "Already exists."; return false; }
            File.WriteAllText(fullPath, "");
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool CreateFolder(string parentDir, string name, out string fullPath)
    {
        fullPath = "";
        var err = ValidateName(name);
        if (err != null) { LastError = err; return false; }
        try
        {
            fullPath = Path.Combine(parentDir, name);
            if (Directory.Exists(fullPath) || File.Exists(fullPath)) { LastError = "Already exists."; return false; }
            Directory.CreateDirectory(fullPath);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool Rename(string source, string newName, out string dest)
    {
        dest = source;
        var err = ValidateName(newName);
        if (err != null) { LastError = err; return false; }
        try
        {
            var dir = Path.GetDirectoryName(source)!;
            dest = Path.Combine(dir, newName);
            if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(dest) || Directory.Exists(dest)) { LastError = "Target already exists."; return false; }
            if (File.Exists(source)) File.Move(source, dest);
            else Directory.Move(source, dest);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool Duplicate(string source, out string dest)
    {
        dest = source;
        try
        {
            if (File.Exists(source))
            {
                var dir = Path.GetDirectoryName(source)!;
                var baseName = Path.GetFileNameWithoutExtension(source);
                var ext = Path.GetExtension(source);
                dest = Path.Combine(dir, $"{baseName} copy{ext}");
                var i = 2;
                while (File.Exists(dest)) dest = Path.Combine(dir, $"{baseName} copy {i++}{ext}");
                File.Copy(source, dest);
                return true;
            }
            if (Directory.Exists(source))
            {
                dest = source.TrimEnd(Path.DirectorySeparatorChar) + " copy";
                var i = 2;
                var d = dest;
                while (Directory.Exists(d)) d = $"{dest} {i++}";
                dest = d;
                CopyDir(source, dest);
                return true;
            }
            LastError = "Source not found.";
            return false;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // Move to .quant/trash for 10s undo, cross platform recycle replacement
    public bool MoveToTrash(string workspaceRoot, string source, out string trashPath, out string undoToken)
    {
        trashPath = "";
        undoToken = Guid.NewGuid().ToString("N");
        try
        {
            var trashDir = Path.Combine(workspaceRoot, ".quant", "trash", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + undoToken[..6]);
            Directory.CreateDirectory(trashDir);
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
            trashPath = Path.Combine(trashDir, name);
            if (File.Exists(source)) File.Move(source, trashPath);
            else if (Directory.Exists(source)) Directory.Move(source, trashPath);
            else { LastError = "Not found."; return false; }
            File.WriteAllText(Path.Combine(trashDir, "origin.txt"), source);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool RestoreFromTrash(string trashPath, string origin)
    {
        try
        {
            if (File.Exists(trashPath)) { Directory.CreateDirectory(Path.GetDirectoryName(origin)!); File.Move(trashPath, origin); }
            else if (Directory.Exists(trashPath)) { Directory.CreateDirectory(Path.GetDirectoryName(origin)!); Directory.Move(trashPath, origin); }
            else return false;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public PasteResult Paste(string destDir, bool keepBoth = true)
    {
        var res = new PasteResult();
        foreach (var src in ClipboardPaths.ToList())
        {
            try
            {
                var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                var dest = Path.Combine(destDir, name);
                if ((File.Exists(dest) || Directory.Exists(dest)) && keepBoth)
                    dest = NextFree(Path.Combine(destDir, name));
                else if (File.Exists(dest) || Directory.Exists(dest))
                {
                    res.Skipped++;
                    continue;
                }
                if (File.Exists(src)) File.Copy(src, dest);
                else if (Directory.Exists(src)) CopyDir(src, dest);
                res.Copied++;
                if (ClipboardIsCut)
                {
                    if (File.Exists(src)) File.Delete(src);
                    else if (Directory.Exists(src)) Directory.Delete(src, true);
                    res.Moved++;
                }
            }
            catch { res.Errors++; }
        }
        if (ClipboardIsCut) { ClipboardPaths.Clear(); ClipboardIsCut = false; }
        return res;
    }

    public sealed class PasteResult { public int Copied; public int Moved; public int Skipped; public int Errors; }

    private static string NextFree(string dest)
    {
        var dir = Path.GetDirectoryName(dest)!;
        var baseName = Path.GetFileNameWithoutExtension(dest);
        var ext = Path.GetExtension(dest);
        var c = Path.Combine(dir, $"{baseName} copy{ext}");
        var i = 2;
        while (File.Exists(c) || Directory.Exists(c)) c = Path.Combine(dir, $"{baseName} copy {i++}{ext}");
        return c;
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
