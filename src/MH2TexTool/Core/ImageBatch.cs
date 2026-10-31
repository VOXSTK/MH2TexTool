using System.Drawing.Imaging;

namespace MH2TexTool.Core;

/// <summary>Пакетная конвертация PNG ↔ BMP (BMP пишется 24-битным, как ждёт редактор).</summary>
public static class ImageBatch
{
    public static readonly string[] SourceExtensions = { ".png", ".bmp", ".jpg", ".jpeg", ".tga", ".dds" };

    public static (int ok, int failed) ConvertFolder(
        string folder,
        bool toPng,
        string? outDir,
        bool recursive,
        Action<string> log,
        CancellationToken token = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

        string filter = toPng ? "*.bmp" : "*.png";
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folder, filter, option);

        if (outDir != null) Directory.CreateDirectory(outDir);

        int ok = 0, failed = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string destDir = outDir ?? Path.GetDirectoryName(file)!;
                string dest = Path.Combine(destDir, Path.GetFileNameWithoutExtension(file) + (toPng ? ".png" : ".bmp"));

                using (var src = LoadBitmap(file))
                {
                    if (toPng)
                    {
                        src.Save(dest, ImageFormat.Png);
                    }
                    else
                    {
                        SaveBmp24(src, dest);
                    }
                }

                ok++;
                log($"{Path.GetFileName(file)} → {Path.GetFileName(dest)} ({GetSizeText(dest)})");
            }
            catch (Exception ex)
            {
                failed++;
                log($"ОШИБКА {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return (ok, failed);
    }

    public static Bitmap LoadBitmap(string path)
    {
        using var fs = File.OpenRead(path);
        using var loaded = new Bitmap(fs);
        return new Bitmap(loaded);
    }

    public static void SaveBmp24(Bitmap src, string path)
    {
        using var bmp24 = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp24))
        {
            g.Clear(System.Drawing.Color.Black);
            g.DrawImageUnscaled(src, 0, 0);
        }
        bmp24.Save(path, ImageFormat.Bmp);
    }

    private static string GetSizeText(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var bmp = new Bitmap(fs);
            return $"{bmp.Width}×{bmp.Height}";
        }
        catch
        {
            return "?";
        }
    }
}
