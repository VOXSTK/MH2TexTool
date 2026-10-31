using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using BCnEnc.Net.Decoder;
using BCnEnc.Net.Encoder;
using BCnEnc.Net.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MH2TexTool.Core;

public sealed class DdsInfo
{
    public int Width;
    public int Height;
    public int MipCount;
    public int BitsPerPixel;
    public int BlockBytes;
    public string FourCc = "";
    public TexCompression Compression;
}

/// <summary>Кодирование/декодирование DDS-блоков (DXT1, DXT5, ARGB32).</summary>
public static class DdsCodec
{
    public const int HeaderSize = 128;
    private const uint FourCcDxt1 = 0x31545844; // 'DXT1'
    private const uint FourCcDxt5 = 0x35545844; // 'DXT5'

    public static bool IsDds(ReadOnlySpan<byte> d) =>
        d.Length >= HeaderSize && d[0] == (byte)'D' && d[1] == (byte)'D' && d[2] == (byte)'S' && d[3] == (byte)' ';

    public static DdsInfo ReadInfo(ReadOnlySpan<byte> dds)
    {
        var info = new DdsInfo();
        if (!IsDds(dds)) return info;

        info.Height = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[0x0C..]);
        info.Width = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[0x10..]);
        info.MipCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[0x1C..]);
        if (info.MipCount <= 0) info.MipCount = 1;

        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(dds[0x54..]);
        info.BitsPerPixel = (int)BinaryPrimitives.ReadUInt32LittleEndian(dds[0x58..]);

        if (fourCc == FourCcDxt1)
        {
            info.FourCc = "DXT1";
            info.Compression = TexCompression.Dxt1;
            info.BitsPerPixel = 4;
            info.BlockBytes = 8;
        }
        else if (fourCc == FourCcDxt5)
        {
            info.FourCc = "DXT5";
            info.Compression = TexCompression.Dxt5;
            info.BitsPerPixel = 8;
            info.BlockBytes = 16;
        }
        else
        {
            info.FourCc = Encoding.ASCII.GetString(dds[0x54..0x58]);
            info.Compression = TexCompression.RawArgb;
            if (info.BitsPerPixel == 0) info.BitsPerPixel = 32;
            info.BlockBytes = 1;
        }
        return info;
    }

    public static int MipDataSize(int width, int height, DdsInfo info)
    {
        long total = 0;
        for (int i = 0; i < Math.Max(1, info.MipCount); i++)
        {
            int w = Math.Max(1, width >> i);
            int h = Math.Max(1, height >> i);
            if (info.Compression == TexCompression.RawArgb)
                total += (long)w * h * info.BitsPerPixel / 8;
            else
                total += (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * info.BlockBytes;
        }
        return (int)Math.Min(int.MaxValue, total);
    }

    // ------------------------------------------------------------- декодирование

    public static Bitmap DecodeToBitmap(byte[] dds)
    {
        var info = ReadInfo(dds);
        if (info.Width <= 0 || info.Height <= 0 || dds.Length < HeaderSize)
            throw new InvalidDataException("Пустой или повреждённый DDS-блок.");

        if (info.Compression == TexCompression.RawArgb)
            return DecodeRaw(dds, info);

        Image<Rgba32> pixels;
        using (var ms = new MemoryStream(dds, false))
        {
            var file = DdsFile.Load(ms);
            pixels = new BcDecoder().Decode(file);
        }

        try
        {
            var bmp = new Bitmap(info.Width, info.Height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, info.Width, info.Height);
            var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    var dst = (byte*)bd.Scan0;
                    for (int y = 0; y < info.Height; y++)
                    {
                        byte* row = dst + (long)y * bd.Stride;
                        for (int x = 0; x < info.Width; x++)
                        {
                            var c = pixels[x, y];
                            row[x * 4 + 0] = c.B;
                            row[x * 4 + 1] = c.G;
                            row[x * 4 + 2] = c.R;
                            row[x * 4 + 3] = c.A;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }
        finally
        {
            pixels.Dispose();
        }
    }

    private static Bitmap DecodeRaw(byte[] dds, DdsInfo info)
    {
        var bmp = new Bitmap(info.Width, info.Height, PixelFormat.Format32bppArgb);
        int rowBytes = info.Width * info.BitsPerPixel / 8;
        var rect = new Rectangle(0, 0, info.Width, info.Height);
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var dst = (byte*)bd.Scan0;
                for (int y = 0; y < info.Height; y++)
                {
                    byte* row = dst + (long)y * bd.Stride;
                    int src = HeaderSize + y * rowBytes;
                    if (src + rowBytes > dds.Length) break;
                    for (int x = 0; x < info.Width; x++)
                    {
                        // маски в этих файлах: R=0x00FF0000 G=0x0000FF00 B=0xFF A=0xFF000000
                        uint px = BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(src + x * 4));
                        row[x * 4 + 0] = (byte)(px & 0xFF);
                        row[x * 4 + 1] = (byte)((px >> 8) & 0xFF);
                        row[x * 4 + 2] = (byte)((px >> 16) & 0xFF);
                        row[x * 4 + 3] = (byte)((px >> 24) & 0xFF);
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }

    // ------------------------------------------------------------- кодирование

    /// <summary>Собирает новый DDS-блок по шаблону заголовка исходной текстуры.</summary>
    /// <param name="maxMips">0 — полная цепочка мипов.</param>
    /// <param name="alphaSource">Оригинальная текстура — из неё берётся альфа, если в источнике её нет
    /// (Upscayl/PNG часто теряют прозрачность).</param>
    /// <param name="maxSize">Ограничение стороны текстуры (0 — без ограничения).</param>
    public static byte[] BuildDds(byte[] templateHeader, Bitmap source, TexCompression compression,
                                  int maxMips = 0, Bitmap? alphaSource = null, int maxSize = 2048)
    {
        if (templateHeader.Length < HeaderSize) templateHeader = DefaultHeader(compression);

        Bitmap? fitted = null;
        Bitmap? merged = null;
        Bitmap work = source;
        try
        {
            if (maxSize > 0 && (source.Width > maxSize || source.Height > maxSize))
            {
                double k = Math.Min((double)maxSize / source.Width, (double)maxSize / source.Height);
                int nw = Math.Max(1, (int)Math.Round(source.Width * k));
                int nh = Math.Max(1, (int)Math.Round(source.Height * k));
                fitted = Resize(source, nw, nh);
                work = fitted;
            }

            if (alphaSource != null && !HasTransparency(work) && HasTransparency(alphaSource))
            {
                merged = MergeAlpha(work, alphaSource);
                work = merged;
            }

            return BuildDdsCore(templateHeader, work, compression, maxMips);
        }
        finally
        {
            merged?.Dispose();
            fitted?.Dispose();
        }
    }

    private static byte[] BuildDdsCore(byte[] templateHeader, Bitmap source, TexCompression compression, int maxMips)
    {
        int w = source.Width, h = source.Height;

        using var ms = new MemoryStream();

        if (compression == TexCompression.RawArgb)
        {
            int mips = maxMips > 0 ? Math.Min(maxMips, FullMipCount(w, h)) : FullMipCount(w, h);
            var header = templateHeader.AsSpan(0, HeaderSize).ToArray();
            PatchHeader(header, w, h, mips, compression);
            ms.Write(header);

            using var current = new Bitmap(source);
            for (int level = 0; level < mips; level++)
            {
                int lw = Math.Max(1, w >> level);
                int lh = Math.Max(1, h >> level);
                Bitmap levelBmp = level == 0 ? current : Resize(current, lw, lh);
                try
                {
                    WriteRaw(ms, levelBmp);
                }
                finally
                {
                    if (!ReferenceEquals(levelBmp, current)) levelBmp.Dispose();
                }
            }
            return ms.ToArray();
        }

        using var image = ToRgbaImage(source);
        var encoder = new BcEncoder();
        encoder.OutputOptions.generateMipMaps = true;
        encoder.OutputOptions.quality = EncodingQuality.Balanced;
        encoder.OutputOptions.format = compression == TexCompression.Dxt1
            ? CompressionFormat.BC1
            : CompressionFormat.BC3;

        var levels = encoder.EncodeToRawBytes(image);
        int useMips = levels.Count;
        if (maxMips > 0) useMips = Math.Min(useMips, maxMips);

        var header2 = templateHeader.AsSpan(0, HeaderSize).ToArray();
        PatchHeader(header2, w, h, useMips, compression);
        ms.Write(header2);
        for (int i = 0; i < useMips; i++)
            ms.Write(levels[i]);

        return ms.ToArray();
    }

    public static int FullMipCount(int w, int h)
    {
        int n = 1;
        while (w > 1 || h > 1)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            n++;
        }
        return n;
    }

    private static Bitmap Resize(Bitmap src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return bmp;
    }

    /// <summary>Есть ли в изображении хоть один пиксель не полностью непрозрачный.</summary>
    public static bool HasTransparency(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var src = (byte*)bd.Scan0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    byte* row = src + (long)y * bd.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                        if (row[x * 4 + 3] != 255) return true;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return false;
    }

    /// <summary>Берёт RGB из src, альфу — из alphaSource (с масштабированием).</summary>
    private static Bitmap MergeAlpha(Bitmap src, Bitmap alphaSource)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
            g.DrawImageUnscaled(src, 0, 0);

        using var scaled = Resize(alphaSource, w, h);
        var rect = new Rectangle(0, 0, w, h);
        var dd = dst.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var da = scaled.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* d = (byte*)dd.Scan0 + (long)y * dd.Stride;
                    byte* a = (byte*)da.Scan0 + (long)y * da.Stride;
                    for (int x = 0; x < w; x++)
                        d[x * 4 + 3] = a[x * 4 + 3];
                }
            }
        }
        finally
        {
            dst.UnlockBits(dd);
            scaled.UnlockBits(da);
        }
        return dst;
    }

    private static void PatchHeader(byte[] header, int w, int h, int mips, TexCompression compression)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x0C), (uint)h);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x10), (uint)w);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x1C), (uint)mips);

        int blockBytes = compression switch
        {
            TexCompression.Dxt1 => 8,
            TexCompression.Dxt5 => 16,
            _ => 0
        };

        if (compression == TexCompression.RawArgb)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x14), (uint)(w * 4));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x50), 0x41);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x54), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x58), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x5C), 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x60), 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x64), 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x68), 0xFF000000);
        }
        else
        {
            int mip0 = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockBytes;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x14), (uint)mip0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x50), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x54),
                compression == TexCompression.Dxt1 ? FourCcDxt1 : FourCcDxt5);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x58), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x5C), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x60), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x64), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x68), 0);
            // caps: TEXTURE | COMPLEX | MIPMAP
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x6C), mips > 1 ? 0x00401008u : 0x00001000u);
        }
    }

    private static byte[] DefaultHeader(TexCompression compression)
    {
        var h = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(h, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x04), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x08), 0x000A1007);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x4C), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x50), compression == TexCompression.RawArgb ? 0x41u : 4u);
        if (compression != TexCompression.RawArgb)
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x54),
                compression == TexCompression.Dxt1 ? FourCcDxt1 : FourCcDxt5);
        return h;
    }

    private static void WriteRaw(Stream s, Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var copy = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(copy))
            g.DrawImageUnscaled(bmp, 0, 0);

        var bd = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var src = (byte*)bd.Scan0;
                var row = new byte[bmp.Width * 4];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy((IntPtr)(src + (long)y * bd.Stride), row, 0, row.Length);
                    s.Write(row, 0, row.Length);
                }
            }
        }
        finally
        {
            copy.UnlockBits(bd);
        }
    }

    /// <summary>Bitmap → ImageSharp Image&lt;Rgba32&gt; (BGRA → RGBA).</summary>
    private static Image<Rgba32> ToRgbaImage(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var copy = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(copy))
            g.DrawImageUnscaled(bmp, 0, 0);

        int w = copy.Width, h = copy.Height;
        var data = new byte[w * h * 4];
        var bd = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var src = (byte*)bd.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = src + (long)y * bd.Stride;
                    int dst = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        data[dst + x * 4 + 0] = row[x * 4 + 2]; // R
                        data[dst + x * 4 + 1] = row[x * 4 + 1]; // G
                        data[dst + x * 4 + 2] = row[x * 4 + 0]; // B
                        data[dst + x * 4 + 3] = row[x * 4 + 3]; // A
                    }
                }
            }
        }
        finally
        {
            copy.UnlockBits(bd);
        }
        return SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(data, w, h);
    }
}
