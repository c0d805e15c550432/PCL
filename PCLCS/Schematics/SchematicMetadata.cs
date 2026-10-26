using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace PCLCS.Schematics;

/// <summary>Bounded Java NBT inspection; block arrays are skipped, never materialized or executed.</summary>
public static class SchematicMetadata
{
    public static string Read(string path, CancellationToken token)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 64L * 1024 * 1024) return "文件超过 64 MB，仅显示文件信息。";
            int first = file.ReadByte(), second = file.ReadByte();
            file.Position = 0;
            using Stream input = first == 0x1f && second == 0x8b ? new GZipStream(file, CompressionMode.Decompress) : file;
            var reader = new Reader(input, token);
            var values = reader.ReadRoot();
            string prefix = values.ContainsKey("Schematic/Width") ? "Schematic/" : "";
            string Get(string key) => values.TryGetValue(prefix + key, out string? value) ? value : "";
            var lines = new List<string>();
            string name = Get("Metadata/Name"), author = Get("Metadata/Author");
            if (name.Length > 0) lines.Add("名称：" + name);
            if (author.Length > 0) lines.Add("作者：" + author);
            string x = Get("Metadata/EnclosingSize/x"), y = Get("Metadata/EnclosingSize/y"), z = Get("Metadata/EnclosingSize/z");
            if (x.Length == 0) { x = Get("Width"); y = Get("Height"); z = Get("Length"); }
            if (x.Length == 0) { x = Get("size/0"); y = Get("size/1"); z = Get("size/2"); }
            if (x.Length == 0 || y.Length == 0 || z.Length == 0)
                return "NBT 可读取，但未识别到投影尺寸；可能是其他 NBT 文件或尚不支持的格式。";
            lines.Add($"尺寸（X × Y × Z）：{x} × {y} × {z}");
            foreach (var pair in new[] {
                ("Metadata/RegionCount", "区域数量"), ("Metadata/TotalBlocks", "方块数量（文件声明）"),
                ("MinecraftDataVersion", "Minecraft 数据版本"), ("DataVersion", "Minecraft 数据版本"),
                ("Version", "格式版本"), ("Metadata/Description", "描述") })
            {
                string value = Get(pair.Item1);
                if (value.Length > 0) lines.Add(pair.Item2 + "：" + value);
            }
            lines.Add("以上为文件元数据；不代表已经验证游戏内兼容性。");
            return string.Join(Environment.NewLine, lines);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException)
        {
            return "无法读取投影元数据：" + ex.Message;
        }
    }

    private sealed class Reader(Stream input, CancellationToken token)
    {
        private const long MaxBytes = 128L * 1024 * 1024;
        private long consumed;
        private int nodes;
        private readonly byte[] buffer = new byte[8192];
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
        private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
        {
            "Width", "Height", "Length", "Version", "DataVersion", "MinecraftDataVersion",
            "Metadata/Name", "Metadata/Author", "Metadata/Description", "Metadata/RegionCount", "Metadata/TotalBlocks",
            "Metadata/EnclosingSize/x", "Metadata/EnclosingSize/y", "Metadata/EnclosingSize/z", "size/0", "size/1", "size/2"
        };

        public Dictionary<string, string> ReadRoot()
        {
            if (Number(1) != 10) throw new InvalidDataException("不是 Java 版 NBT 复合标签。");
            Text(); // The named root is not part of its children's paths (Sponge v1/v2).
            Payload(10, "", 0);
            return values;
        }

        private void ReadBytes(byte[] destination, int count)
        {
            token.ThrowIfCancellationRequested();
            if (count > MaxBytes - consumed) throw new InvalidDataException("解压数据超过 128 MB 检查上限。");
            int offset = 0;
            while (offset < count)
            {
                token.ThrowIfCancellationRequested();
                int read = input.Read(destination, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("NBT 数据不完整。");
                offset += read;
                consumed += read;
            }
        }

        private long Number(int bytes)
        {
            ReadBytes(buffer, bytes);
            long result = 0;
            for (int i = 0; i < bytes; i++) result = unchecked((result << 8) | buffer[i]);
            return result;
        }

        private int Count()
        {
            int count = unchecked((int)Number(4));
            if (count < 0) throw new InvalidDataException("NBT 数组长度为负数。");
            return count;
        }

        private string Text()
        {
            int count = (int)Number(2);
            var bytes = new byte[count]; // NBT strings have a 16-bit byte length.
            ReadBytes(bytes, count);
            // Java's modified UTF-8 encodes supplementary characters as surrogate pairs.
            var result = new StringBuilder();
            for (int i = 0; i < bytes.Length;)
            {
                int a = bytes[i++];
                if (a < 128) result.Append((char)a);
                else if ((a & 0xe0) == 0xc0 && i < bytes.Length)
                    result.Append((char)(((a & 31) << 6) | Continuation(bytes[i++])));
                else if ((a & 0xf0) == 0xe0 && i + 1 < bytes.Length)
                    result.Append((char)(((a & 15) << 12) | (Continuation(bytes[i++]) << 6) | Continuation(bytes[i++])));
                else if ((a & 0xf8) == 0xf0 && i + 2 < bytes.Length)
                    result.Append(char.ConvertFromUtf32(((a & 7) << 18) | (Continuation(bytes[i++]) << 12) |
                        (Continuation(bytes[i++]) << 6) | Continuation(bytes[i++])));
                else throw new InvalidDataException("NBT 字符串编码无效。");
            }
            return result.ToString();
        }

        private static int Continuation(byte value) => (value & 0xc0) == 0x80 ? value & 63 :
            throw new InvalidDataException("NBT 字符串编码无效。");

        private void Skip(long count)
        {
            if (count > MaxBytes - consumed) throw new InvalidDataException("NBT 数组超过检查上限。");
            while (count > 0)
            {
                int chunk = (int)Math.Min(count, buffer.Length);
                ReadBytes(buffer, chunk);
                count -= chunk;
            }
        }

        private void Payload(int type, string path, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64 || ++nodes > 1000000) throw new InvalidDataException("NBT 嵌套或标签数量超过检查上限。");
            string key = path.StartsWith("Schematic/", StringComparison.Ordinal) ? path.Substring(10) : path;
            bool capture = Fields.Contains(key);
            long number;
            switch (type)
            {
                case 1: number = unchecked((sbyte)Number(1)); break;
                case 2:
                    number = Number(2);
                    if (key != "Width" && key != "Height" && key != "Length") number = unchecked((short)number);
                    break;
                case 3: number = unchecked((int)Number(4)); break;
                case 4: number = Number(8); break;
                case 5: Skip(4); return;
                case 6: Skip(8); return;
                case 7: Skip(Count()); return;
                case 8:
                    string text = Text();
                    if (capture) values[path] = text.Length > 4096 ? text.Substring(0, 4096) + "…" : text;
                    return;
                case 9:
                    int child = (int)Number(1), count = Count();
                    if (child < 0 || child > 12 || (child == 0 && count != 0)) throw new InvalidDataException("NBT 列表类型无效。");
                    if (count > 1000000) throw new InvalidDataException("NBT 列表过大。");
                    for (int i = 0; i < count; i++) Payload(child, path == "size" && i < 3 ? "size/" + i : "#", depth + 1);
                    return;
                case 10:
                    while (true)
                    {
                        int next = (int)Number(1);
                        if (next == 0) return;
                        string name = Text();
                        // Keep only metadata-relevant paths; block/entity compounds cannot grow retained state.
                        string nextPath = path.Length == 0 ? name : path + "/" + name;
                        if (!(Fields.Contains(nextPath) || nextPath == "Metadata" || nextPath == "Metadata/EnclosingSize" ||
                              nextPath == "size" || nextPath == "Schematic" || nextPath.StartsWith("Schematic/Metadata", StringComparison.Ordinal) ||
                              (nextPath.StartsWith("Schematic/", StringComparison.Ordinal) && Fields.Contains(nextPath.Substring(10)))))
                            nextPath = "#";
                        Payload(next, nextPath, depth + 1);
                    }
                case 11: Skip((long)Count() * 4); return;
                case 12: Skip((long)Count() * 8); return;
                default: throw new InvalidDataException("未知 NBT 标签类型：" + type);
            }
            if (capture) values[path] = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
