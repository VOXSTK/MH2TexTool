using System.Diagnostics;
using System.Text;

namespace MH2TexTool.Core;

/// <summary>Командный режим — для пакетной обработки и автотестов.</summary>
public static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--selftest" => SelfTest(args[1]),
                "--export" => Export(args[1], args[2], args.Length > 3 ? args[3] : "png"),
                "--import" => Import(args[1], args[2], args[3]),
                "--export-tree" => ExportTree(args[1], args[2]),
                "--import-tree" => ImportTree(args[1], args[2], args[3]),
                "--check" => Check(args[1]),
                _ => Usage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ОШИБКА: " + ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("MH2TexTool — командный режим");
        Console.WriteLine("  --selftest <file.tex>                 round-trip без изменений");
        Console.WriteLine("  --check <file.tex>                    сводка по текстурам");
        Console.WriteLine("  --export <file.tex> <folder> [png|bmp]");
        Console.WriteLine("  --import <file.tex> <folder> <out.tex>");
        Console.WriteLine("  --export-tree <texRoot> <outRoot>      PNG по папкам: <out>/<rel>/<tex>/имя.png");
        Console.WriteLine("  --import-tree <texRoot> <pngRoot> <outRoot>");
        return 1;
    }

    private static int SelfTest(string path)
    {
        var original = File.ReadAllBytes(path);
        var file = TexFile.Load(path);
        var rebuilt = file.Build(TexContainerKind.Preserve);

        byte[] a = Unwrap(original), b = Unwrap(rebuilt);
        bool same = a.AsSpan().SequenceEqual(b);
        Console.WriteLine($"{Path.GetFileName(path)}: контейнер {(file.SourceKind == TexContainerKind.Z2hm ? "Z2HM" : "TCDT")}, " +
                          $"текстур {file.Textures.Count}, оригинал {original.Length:N0} Б, новый {rebuilt.Length:N0} Б");
        Console.WriteLine(same ? "  payload: идентичен" : "  payload: ОТЛИЧАЕТСЯ");

        if (!same && a.Length == b.Length)
        {
            int diffs = 0, first = -1;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                diffs++;
                if (first < 0) first = i;
            }
            Console.WriteLine($"  различий: {diffs}, первое по смещению 0x{first:X}");
        }

        return same ? 0 : 3;
    }

    private static byte[] Unwrap(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes[0] == (byte)'Z' && bytes[1] == (byte)'2')
        {
            using var input = new MemoryStream(bytes, 8, bytes.Length - 8);
            using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }
        return bytes;
    }

    private static int Check(string path)
    {
        var file = TexFile.Load(path);
        Console.WriteLine($"{Path.GetFileName(path)}: {(file.SourceKind == TexContainerKind.Z2hm ? "Z2HM" : "TCDT")}, текстур {file.Textures.Count}");
        foreach (var tex in file.Textures)
        {
            var info = DdsCodec.ReadInfo(tex.CurrentDds);
            Console.WriteLine($"  {tex.Name,-30} {info.Width,5}x{info.Height,-5} {tex.FormatName,-6} mips={info.MipCount,-2} " +
                              $"off=0x{tex.DataOffset:X8} size={tex.DataSize}");
        }
        return 0;
    }

    private static int Export(string path, string folder, string format)
    {
        var file = TexFile.Load(path);
        Directory.CreateDirectory(folder);
        bool png = !format.Equals("bmp", StringComparison.OrdinalIgnoreCase);
        int ok = 0, fail = 0;
        var occurrences = new Occurrences();
        foreach (var tex in file.Textures)
        {
            try
            {
                using var bmp = DdsCodec.DecodeToBitmap(tex.CurrentDds);
                string baseName = SafeName(tex.Name);
                string name = occurrences.Next(baseName);
                string dest = Path.Combine(folder, name + (png ? ".png" : ".bmp"));
                if (png) bmp.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
                else ImageBatch.SaveBmp24(bmp, dest);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Console.Error.WriteLine($"  {tex.Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"Экспорт: {ok} ok, {fail} ошибок → {folder}");
        return fail == 0 ? 0 : 4;
    }

    public static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "texture" : name.Trim();
    }

    private static int Import(string path, string folder, string outPath)
    {
        var file = TexFile.Load(path);
        var images = TextureFolder.Scan(folder);

        int replaced = 0, missing = 0, failed = 0;
        var missingNames = new List<string>();
        var occurrences = new Occurrences();

        foreach (var tex in file.Textures)
        {
            string key = occurrences.Next(SafeName(tex.Name));
            var src = images.Find(key, soft: !key.Contains('~'));
            if (src == null)
            {
                missing++;
                missingNames.Add(tex.Name);
                continue;
            }

            try
            {
                byte[] block;
                var (dw, dh) = (0, 0);
                if (src.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    block = File.ReadAllBytes(src);
                    if (!DdsCodec.IsDds(block)) throw new InvalidDataException("не DDS");
                }
                else
                {
                    using var bmp = ImageBatch.LoadBitmap(src);
                    (dw, dh) = (bmp.Width, bmp.Height);
                    using var alpha = LoadAlphaSource(tex, bmp);
                    block = DdsCodec.BuildDds(tex.OriginalDds, bmp, tex.Compression, 0, alpha, MaxSize);
                }

                tex.Replacement = block;
                var info = DdsCodec.ReadInfo(block);
                replaced++;
                if (info.Width != tex.OriginalWidth || info.Height != tex.OriginalHeight)
                    Console.WriteLine($"  {tex.Name}: {tex.OriginalWidth}x{tex.OriginalHeight} → {info.Width}x{info.Height}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  {tex.Name}: {ex.Message}");
            }
        }

        if (missing > 0)
            Console.WriteLine("Нет файлов для: " + string.Join(", ", missingNames.Take(20)) +
                              (missingNames.Count > 20 ? $" … (ещё {missingNames.Count - 20})" : ""));

        var bytes = file.Build(TexContainerKind.Preserve);
        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"Импорт: заменено {replaced}, не найдено {missing}, ошибок {failed}");
        Console.WriteLine($"Записано: {outPath} ({bytes.Length:N0} Б)");
        return failed == 0 ? 0 : 4;
    }

    /// <summary>Предел стороны текстуры при импорте (32-битный движок, 2 ГБ памяти).</summary>
    public static int MaxSize = 2048;

    /// <summary>Если PNG потерял прозрачность (Upscayl), берём альфу из оригинала.</summary>
    public static Bitmap? LoadAlphaSource(TexTexture tex, Bitmap source)
    {
        if (tex.Compression != TexCompression.Dxt5 && tex.Compression != TexCompression.RawArgb)
            return null;
        if (DdsCodec.HasTransparency(source))
            return null;

        var original = DdsCodec.DecodeToBitmap(tex.OriginalDds);
        if (DdsCodec.HasTransparency(original))
            return original;
        original.Dispose();
        return null;
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).ToArray());

    // ------------------------------------------------------------- дерево папок

    private static int ExportTree(string texRoot, string outRoot)
    {
        var texes = Directory.GetFiles(texRoot, "*.tex", SearchOption.AllDirectories);
        Console.WriteLine($"Файлов .tex: {texes.Length}");
        int failed = 0;
        foreach (var tex in texes)
        {
            string rel = Path.GetRelativePath(texRoot, Path.GetDirectoryName(tex)!);
            string dir = Path.Combine(outRoot, rel, Path.GetFileNameWithoutExtension(tex));
            try
            {
                Export(tex, dir, "png");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"{tex}: {ex.Message}");
            }
        }
        Console.WriteLine($"Готово: {texes.Length - failed} файлов, ошибок {failed} → {outRoot}");
        return failed == 0 ? 0 : 4;
    }

    /// <summary>Папка с картинками: сама папка или вложенная (Upscayl пишет результат
    /// в подпапку вида «upscayl_png_upscayl-standard-4x_4x»).</summary>
    private static string? ResolveImageDir(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        if (TextureFolder.CountImages(dir) > 0) return dir;

        string? best = null;
        int bestCount = 0;
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
        {
            int count = TextureFolder.CountImages(sub);
            if (count > bestCount)
            {
                bestCount = count;
                best = sub;
            }
        }
        return best;
    }

    private static int ImportTree(string texRoot, string pngRoot, string outRoot)
    {
        var texes = Directory.GetFiles(texRoot, "*.tex", SearchOption.AllDirectories);
        Console.WriteLine($"Файлов .tex: {texes.Length}");
        int done = 0, skipped = 0, failed = 0;

        foreach (var tex in texes)
        {
            string rel = Path.GetRelativePath(texRoot, Path.GetDirectoryName(tex)!);
            string dir = Path.Combine(pngRoot, rel, Path.GetFileNameWithoutExtension(tex));
            string dest = Path.Combine(outRoot, rel, Path.GetFileName(tex));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            string? imageDir = ResolveImageDir(dir);
            if (imageDir == null)
            {
                skipped++;
                File.Copy(tex, dest, true);
                continue;
            }

            try
            {
                Console.WriteLine($"== {Path.GetFileName(tex)} ← {imageDir}");
                Import(tex, imageDir, dest);
                done++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"{tex}: {ex.Message}");
            }
        }

        Console.WriteLine($"Готово: обработано {done}, без PNG {skipped}, ошибок {failed} → {outRoot}");
        return failed == 0 ? 0 : 4;
    }
}
