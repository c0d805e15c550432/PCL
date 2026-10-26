using System.IO.Compression;
using System.Text;
using PCLCS.Schematics;

// Independent NBT fixtures exercise format boundaries and file operations without Minecraft or a user's instance.
internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
        Console.WriteLine("PASS " + message);
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (IOException) { rejected = true; }
        Check(rejected, message);
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        try
        {
            CheckModDetection(Path.Combine(root, "mod-checks"));
            byte[] lite = Compound("", Compound("Metadata", String("Name", "中文投影"), String("Author", "作者"),
                Tag(8, "Description", new byte[] { 0, 6, 0xed, 0xa0, 0xbd, 0xed, 0xb8, 0x80 }),
                Compound("EnclosingSize", Int("x", 12), Int("y", 34), Int("z", 56)), Int("TotalBlocks", 7), Int("RegionCount", 1)),
                Int("Version", 6), Int("MinecraftDataVersion", 3465),
                Tag(12, "SkippedLongArray", Join(Be(2, 4), Be(1, 8), Be(-1, 8))));
            string input = Write(root, "测试.litematic", lite, true);
            string metadata = SchematicMetadata.Read(input, CancellationToken.None);
            Check(metadata.Contains("中文投影") && metadata.Contains("作者") && metadata.Contains("12 × 34 × 56") && metadata.Contains("😀"), "gzip litematic and Java modified UTF-8");
            Check(metadata.Contains("方块数量（文件声明）：7") && metadata.Contains("3465"), "litematic counts and data version");
            byte[][] dimensions = { Short("Width", 65535), Short("Height", 2), Short("Length", 3), Int("Version", 3) };
            string sponge = Write(root, "v3.schem", Compound("", Compound("Schematic", dimensions)), true);
            Check(SchematicMetadata.Read(sponge, CancellationToken.None).Contains("65535 × 2 × 3"), "Sponge v3 nested compound, unsigned dimensions");
            string legacy = Write(root, "legacy.schematic", Compound("Schematic", dimensions), false);
            Check(SchematicMetadata.Read(legacy, CancellationToken.None).Contains("65535 × 2 × 3"), "named-root legacy / Sponge v2 and uncompressed NBT");
            string structure = Write(root, "structure.nbt", Compound("", Tag(9, "size", Join(new byte[] { 3 }, Be(3, 4), Be(4, 4), Be(5, 4), Be(6, 4)))), true);
            Check(SchematicMetadata.Read(structure, CancellationToken.None).Contains("4 × 5 × 6"), "vanilla structure dimensions");
            string truncated = Write(root, "truncated.nbt", lite.Take(lite.Length - 2).ToArray(), false);
            Check(SchematicMetadata.Read(truncated, CancellationToken.None).StartsWith("无法读取"), "truncated NBT does not crash");
            string negative = Write(root, "negative.nbt", Compound("", Tag(7, "data", Be(-1, 4))), false);
            Check(SchematicMetadata.Read(negative, CancellationToken.None).Contains("负数"), "negative array length rejected");
            string oversized = Write(root, "oversized.nbt", Compound("", Tag(12, "data", Be(int.MaxValue, 4))), false);
            Check(SchematicMetadata.Read(oversized, CancellationToken.None).Contains("上限"), "large arrays rejected before allocation");
            byte[] deep = Int("x", 1);
            for (int i = 0; i < 67; i++) deep = Compound("nested", deep);
            Check(SchematicMetadata.Read(Write(root, "deep.nbt", deep, true), CancellationToken.None).Contains("上限"), "deep compounds bounded");
            Check(SchematicMetadata.Read(Write(root, "other.nbt", Compound("", Int("foo", 1)), true), CancellationToken.None).Contains("未识别"), "unrelated NBT not claimed as a schematic");
            bool canceled = false;
            try { SchematicMetadata.Read(input, new CancellationToken(true)); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "metadata cancellation");

            var library = new SchematicLibrary(Path.Combine(root, "instance-a"));
            Check(library.Scan(CancellationToken.None, new List<string>()).Count == 0 && !Directory.Exists(library.Root), "empty scan does not create directories");
            string imported = library.Import(input);
            Check(File.ReadAllBytes(imported).SequenceEqual(File.ReadAllBytes(input)), "import preserves every source byte");
            Reject(() => library.Import(input), "duplicate import never overwrites");
            string renamed = library.Rename(imported, "重命名");
            Check(File.Exists(renamed) && !File.Exists(imported), "Unicode rename preserves extension");
            Reject(() => library.Rename(renamed, "..\\escape"), "rename rejects path traversal");
            Reject(() => library.Rename(renamed, "CON"), "rename rejects device names");
            Reject(() => library.Rename(renamed, "trail."), "rename rejects trailing dot");
            Reject(() => library.ValidateFile(input), "operations reject paths outside instance");
            string exported = Path.Combine(root, "exported.litematic");
            library.Export(renamed, exported);
            Check(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(input)), "export is byte-identical");
            string nestedExport = Path.Combine(root, "export-batch", "nested", "copy.litematic");
            library.Export(renamed, nestedExport);
            Check(File.ReadAllBytes(nestedExport).SequenceEqual(File.ReadAllBytes(input)), "batch export creates nested destination without changing bytes");
            Reject(() => library.Export(renamed, exported), "export never overwrites");
            Reject(() => library.Export(renamed, Path.Combine(root, "wrong.schem")), "export cannot disguise format conversion");
            Directory.CreateDirectory(Path.Combine(library.Root, "nested"));
            File.Copy(sponge, Path.Combine(library.Root, "nested", "nested.SCHEM"));
            File.WriteAllText(Path.Combine(library.Root, "ignored.txt"), "ignored");
            var files = library.Scan(CancellationToken.None, new List<string>());
            Check(files.Count == 2 && files.Any(f => f.RelativePath == @"nested\nested.SCHEM"), "recursive case-insensitive inventory excludes unrelated files");
            var other = new SchematicLibrary(Path.Combine(root, "instance-b"));
            Check(other.Scan(CancellationToken.None, new List<string>()).Count == 0, "isolated game directories do not mix libraries");
            var shared = new SchematicLibrary(Path.Combine(root, "instance-a"));
            Check(shared.Scan(CancellationToken.None, new List<string>()).Count == 2, "shared game directories share the library");
            // Optional real-world files are read only, never imported or modified.
            foreach (string real in args.Skip(1))
            {
                string text = SchematicMetadata.Read(real, CancellationToken.None);
                Check(text.Contains("尺寸（X × Y × Z）"), "real schematic " + Path.GetFileName(real));
                Console.WriteLine(text);
            }
            Console.WriteLine($"PASS {assertions} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void CheckModDetection(string game)
    {
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "no mods directory hides schematic entry");
        string mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        string archivePath = Path.Combine(mods, "renamed.jar");
        void Fixture(string entry, string data)
        {
            using var file = File.Create(archivePath);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(data);
        }
        foreach (var fixture in new[] {
            ("fabric.mod.json", "{\"id\":\"litematica\"}"),
            ("quilt.mod.json", "{\"quilt_loader\":{\"id\":\"litematica\"}}"),
            ("mcmod.info", "[{\"modid\":\"schematica\"}]"),
            ("META-INF/mods.toml", "[[mods]]\nmodId=\"forgematica\""),
            ("META-INF/neoforge.mods.toml", "[[mods]] # comment\nmodId = 'forgematica' # comment") })
        {
            Fixture(fixture.Item1, fixture.Item2);
            Check(SchematicModDetector.IsInstalled(game, CancellationToken.None), "detect mod declaration in " + fixture.Item1);
        }
        File.Move(archivePath, archivePath + ".disabled");
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "disabled mod is excluded");
        foreach (string id in new[] { "malilib", "syncmatica", "litematica-printer" })
        {
            Fixture("fabric.mod.json", "{\"id\":\"" + id + "\",\"depends\":{\"litematica\":\"*\"}}");
            Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "dependency or addon alone does not count: " + id);
        }
        Fixture("META-INF/mods.toml", "[[mods]]\nmodId='addon'\n[[dependencies.addon]]\nmodId='litematica'");
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "Forge dependency declaration does not count");
        Fixture("META-INF/mods.toml", "[[mods]]\nmodId='addon'\ndescription='''\n[[mods]]\nmodId='litematica'\n'''");
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "multiline description does not count as declaration");
        Fixture("fabric.mod.json", "{broken");
        File.Move(archivePath, Path.Combine(mods, "litematica.jar"));
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "filename and malformed metadata cannot enable entry");
        File.WriteAllText(archivePath, "not a zip");
        Check(!SchematicModDetector.IsInstalled(game, CancellationToken.None), "broken archive is skipped");
        var cancellation = new CancellationToken(true);
        bool cancelled = false;
        try { SchematicModDetector.IsInstalled(game, cancellation); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "mod scan honors cancellation");
    }

    private static string Write(string root, string name, byte[] bytes, bool gzip)
    {
        string path = Path.Combine(root, name);
        using var file = File.Create(path);
        if (gzip) { using var zip = new GZipStream(file, CompressionMode.Compress); zip.Write(bytes, 0, bytes.Length); }
        else file.Write(bytes, 0, bytes.Length);
        return path;
    }

    private static byte[] Be(long value, int length) => Enumerable.Range(0, length).Select(i => (byte)(value >> (8 * (length - i - 1)))).ToArray();
    private static byte[] Join(params byte[][] arrays) => arrays.SelectMany(x => x).ToArray();
    private static byte[] Utf(string value) { byte[] b = Encoding.UTF8.GetBytes(value); return Join(Be(b.Length, 2), b); }
    private static byte[] Tag(byte type, string name, byte[] body) => Join(new[] { type }, Utf(name), body);
    private static byte[] Int(string name, int value) => Tag(3, name, Be(value, 4));
    private static byte[] Short(string name, int value) => Tag(2, name, Be(value, 2));
    private static byte[] String(string name, string value) => Tag(8, name, Utf(value));
    private static byte[] Compound(string name, params byte[][] children) => Tag(10, name, Join(Join(children), new byte[] { 0 }));
}
