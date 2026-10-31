using System.Text.RegularExpressions;

namespace MH2TexTool.Core;

/// <summary>Индекс изображений в папке: поиск по имени текстуры, в т.ч. после Upscayl
/// (тот добавляет суффиксы вида «_upscayl_4x»), с игнорированием регистра/подчёркиваний.</summary>
public sealed class TextureFolder
{
    public static readonly string[] Extensions = { ".png", ".bmp", ".dds", ".jpg", ".jpeg", ".tga" };

    private readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _normalized = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _exact.Count;

    public static TextureFolder Scan(string folder)
    {
        var index = new TextureFolder();
        foreach (var path in Directory.GetFiles(folder))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!Extensions.Contains(ext)) continue;

            string stem = Path.GetFileNameWithoutExtension(path);
            index._exact.TryAdd(stem, path);
            index._normalized.TryAdd(Normalize(StripUpscaleSuffix(stem)), path);
        }
        return index;
    }

    /// <summary>Сколько изображений лежит прямо в папке.</summary>
    public static int CountImages(string folder) =>
        Directory.GetFiles(folder).Count(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()));

    public string? Find(string textureName, bool soft = true)
    {
        if (_exact.TryGetValue(textureName, out var path)) return path;
        if (!soft) return null;
        if (_normalized.TryGetValue(Normalize(textureName), out path)) return path;
        return _normalized.TryGetValue(Normalize(StripUpscaleSuffix(textureName)), out path) ? path : null;
    }

    public static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).ToArray());

    public static string StripUpscaleSuffix(string name)
    {
        // обрезаем всё, что Upscayl дописывает после имени файла
        foreach (var marker in new[] { "upscayl", "upscaled", "upscale" })
        {
            int i = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i > 0)
            {
                name = name[..i];
                break;
            }
        }

        name = Regex.Replace(name, @"[ _\-]+(\d+x|x\d+)$", "", RegexOptions.IgnoreCase);
        return name.Trim(' ', '_', '-', '.');
    }
}

/// <summary>Уникальные имена для повторяющихся в одном .tex текстур: name, name~2, name~3 …</summary>
public sealed class Occurrences
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public string Next(string name)
    {
        _counts.TryGetValue(name, out int n);
        n++;
        _counts[name] = n;
        return n == 1 ? name : $"{name}~{n}";
    }
}
