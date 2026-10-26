using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCLCS.Schematics;

/// <summary>Read installed mod declarations, never filenames, dependencies, classes or executable code.</summary>
public static class SchematicModDetector
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        { "litematica", "forgematica", "schematica" };

    public static bool IsInstalled(string gameDirectory, CancellationToken token)
    {
        string folder = Path.Combine(gameDirectory, "mods");
        if (!Directory.Exists(folder)) return false;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder).Take(10000))
            {
                token.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(file);
                // PCL disabled/old files are deliberately excluded.
                if (!(extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".litemod", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    foreach (string name in new[] { "fabric.mod.json", "quilt.mod.json", "mcmod.info", "litemod.json",
                        "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = archive.GetEntry(name);
                        if (entry == null || entry.Length > 1024 * 1024) continue;
                        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                        string text = reader.ReadToEnd();
                        if (name.EndsWith(".toml", StringComparison.Ordinal))
                        {
                            // Only modId inside [[mods]], never [[dependencies.*]].
                            bool modTable = false;
                            bool multiline = false;
                            foreach (string line in text.Split('\n'))
                            {
                                string trimmed = line.Trim();
                                if (trimmed.Contains("\"\"\"") || trimmed.Contains("'''"))
                                {
                                    if (Regex.Matches(trimmed, "\"\"\"|'''").Count % 2 != 0) multiline = !multiline;
                                    continue;
                                }
                                if (multiline) continue;
                                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                                    modTable = Regex.IsMatch(trimmed, @"^\[\[\s*mods\s*\]\]\s*(#.*)?$");
                                if (!modTable) continue;
                                var match = Regex.Match(trimmed, "^modId\\s*=\\s*[\"']([a-zA-Z0-9_-]+)[\"']\\s*(#.*)?$");
                                if (match.Success && Supported.Contains(match.Groups[1].Value)) return true;
                            }
                        }
                        else
                        {
                            using var jsonReader = new JsonTextReader(new StringReader(text)) { MaxDepth = 32 };
                            var json = JToken.ReadFrom(jsonReader);
                            if (name == "fabric.mod.json" && Supported.Contains((string?)json["id"] ?? "")) return true;
                            if (name == "quilt.mod.json" && Supported.Contains((string?)json["quilt_loader"]?["id"] ?? "")) return true;
                            if (name == "litemod.json" && Supported.Contains((string?)json["name"] ?? "")) return true;
                            if (name == "mcmod.info")
                            {
                                var list = json as JArray ?? json["modList"] as JArray;
                                if (list != null && list.OfType<JObject>().Any(mod => Supported.Contains((string?)mod["modid"] ?? ""))) return true;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                                           ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    // A damaged, locked or non-mod archive cannot establish that a schematic mod is installed.
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        return false;
    }
}
