using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PCLCS.Schematics;

/// <summary>Local, non-overwriting file operations scoped to one instance's schematics folder.</summary>
public sealed class SchematicLibrary
{
    public string Root { get; }
    public const string FileFilter = "投影文件|*.litematic;*.schematic;*.schem;*.nbt";

    public SchematicLibrary(string gameDirectory)
    {
        Root = Path.GetFullPath(Path.Combine(gameDirectory, "schematics")).TrimEnd(Path.DirectorySeparatorChar);
    }

    public static bool IsSupported(string path) =>
        new[] { ".litematic", ".schematic", ".schem", ".nbt" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // Reject links/junctions in all existing ancestors, including a redirected root.
    private static void CheckLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不支持符号链接或目录联接，请使用实际文件夹。\n" + current);
        }
    }

    public void EnsureDirectory()
    {
        CheckLinks(Root);
        Directory.CreateDirectory(Root);
    }

    public string ValidateFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !IsSupported(full))
            throw new IOException("文件不属于当前版本的投影目录。");
        CheckLinks(full);
        if (!File.Exists(full)) throw new FileNotFoundException("投影已被移动或删除，请刷新列表。", full);
        return full;
    }

    public List<SchematicFile> Scan(CancellationToken token, IList<string> warnings)
    {
        CheckLinks(Root);
        var result = new List<SchematicFile>();
        if (!Directory.Exists(Root)) return result;
        int visited = 0;
        Visit(Root, 0);
        return result.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();

        void Visit(string folder, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 32 || visited >= 20000)
            {
                if (warnings.Count < 20) warnings.Add("目录过大或层级过深，部分内容未列出。");
                return;
            }
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 20000) { warnings.Add("已达到 20000 个目录项的扫描上限。"); break; }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            if (warnings.Count < 20) warnings.Add("已跳过链接：" + path);
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0) Visit(path, depth + 1);
                        else if (IsSupported(path)) result.Add(new SchematicFile(path, Root));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        if (warnings.Count < 20) warnings.Add(path + "：" + ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (warnings.Count < 20) warnings.Add(folder + "：" + ex.Message);
            }
        }
    }

    public string Import(string source)
    {
        if (!IsSupported(source)) throw new IOException("仅支持 litematic、schematic、schem 和 nbt 文件。");
        CheckLinks(source);
        EnsureDirectory();
        string target = Path.Combine(Root, Path.GetFileName(source));
        CopyNew(source, target);
        return target;
    }

    public string Rename(string source, string newStem)
    {
        source = ValidateFile(source);
        if (string.IsNullOrWhiteSpace(newStem) || newStem != newStem.Trim() || newStem.EndsWith(".") ||
            newStem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("文件名不能为空，不能包含路径、非法字符或末尾空格/句点。");
        string device = newStem.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(device) ||
            System.Text.RegularExpressions.Regex.IsMatch(device, @"^(COM|LPT)[0-9¹²³]$"))
            throw new IOException("不能使用 Windows 保留设备名。");
        string target = Path.Combine(Path.GetDirectoryName(source)!, newStem + Path.GetExtension(source));
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            if (source == target) return source;
            throw new IOException("若只改变大小写，请先改为另一个文件名。");
        }
        CheckLinks(target);
        File.Move(source, target); // File.Move never replaces an existing schematic.
        return target;
    }

    public void Export(string source, string target)
    {
        source = ValidateFile(source);
        if (!string.Equals(Path.GetExtension(source), Path.GetExtension(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("导出只复制文件，不转换格式，请保留原扩展名。");
        CheckLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        CopyNew(source, Path.GetFullPath(target));
    }

    private static void CopyNew(string source, string target)
    {
        if (File.Exists(target)) throw new IOException("同名文件已存在，未覆盖：" + Path.GetFileName(target));
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".pcl-import-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary, false);
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
