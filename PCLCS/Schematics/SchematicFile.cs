using System;
using System.IO;

namespace PCLCS.Schematics;

/// <summary>A file inventory entry. Inspecting a schematic never loads its blocks into a world.</summary>
public sealed class SchematicFile
{
    public string FullPath { get; }
    public string RelativePath { get; }
    public string Name => Path.GetFileName(FullPath);
    public string Format => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public long Length { get; }
    public DateTime Modified { get; }
    public string SizeText => Length < 1048576 ? $"{Length / 1024.0:N1} KB" : $"{Length / 1048576.0:N1} MB";
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

    public SchematicFile(string path, string root)
    {
        var info = new FileInfo(path);
        FullPath = info.FullName;
        RelativePath = FullPath.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
        Length = info.Length;
        Modified = info.LastWriteTime;
    }
}
