using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MH2TexTool.Core;

public enum TexContainerKind
{
    Preserve,
    Tcdt,
    Z2hm
}

public enum TexCompression
{
    Dxt1,
    Dxt5,
    RawArgb
}

/// <summary>Одна текстура внутри .tex (узел в связном списке + блок DDS).</summary>
public sealed class TexTexture
{
    public int NodeOffset { get; set; }
    public string Name { get; set; } = "";
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint OriginalWidth { get; set; }
    public uint OriginalHeight { get; set; }
    public uint BitsPerPixel { get; set; }
    public uint Pitch { get; set; }
    public uint BlockBytes { get; set; }
    public ushort MipCount { get; set; }
    public uint DataOffset { get; set; }
    public byte FormatByte { get; set; }
    public uint DataSize { get; set; }

    /// <summary>Исходный блок DDS (128 байт заголовка + данные), как лежит в файле.</summary>
    public byte[] OriginalDds { get; set; } = Array.Empty<byte>();

    /// <summary>Новый блок DDS, если текстура заменена.</summary>
    public byte[]? Replacement { get; set; }

    public TexCompression Compression { get; set; }

    public bool IsModified => Replacement != null;

    public byte[] CurrentDds => Replacement ?? OriginalDds;

    public string FormatName => Compression switch
    {
        TexCompression.Dxt1 => "DXT1",
        TexCompression.Dxt5 => "DXT5",
        _ => "ARGB32"
    };

    public string SizeText => $"{Width}×{Height}";

    public override string ToString() => Name;
}

/// <summary>Чтение и запись контейнера TCDT / Z2HM (Manhunt 2 .tex).</summary>
public sealed class TexFile
{
    public const int HeaderSize = 0x40;
    public const int NodeSize = 0x80;
    public const int SentinelOffset = 0x24;

    private byte[] _payload = Array.Empty<byte>();   // без таблицы релокаций (она в конце)
    private byte[] _reloc = Array.Empty<byte>();

    public TexContainerKind SourceKind { get; private set; } = TexContainerKind.Tcdt;
    public string SourcePath { get; private set; } = "";
    public List<TexTexture> Textures { get; } = new();

    public uint RelocCount { get; private set; }

    public int ModifiedCount => Textures.Count(t => t.IsModified);

    public static TexFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var file = new TexFile { SourcePath = path };

        byte[] raw;
        if (bytes.Length > 8 && bytes[0] == (byte)'Z' && bytes[1] == (byte)'2' && bytes[2] == (byte)'H' && bytes[3] == (byte)'M')
        {
            file.SourceKind = TexContainerKind.Z2hm;
            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
            using var input = new MemoryStream(bytes, 8, bytes.Length - 8);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(declared, 64 * 1024 * 1024));
            z.CopyTo(output);
            raw = output.ToArray();
        }
        else
        {
            file.SourceKind = TexContainerKind.Tcdt;
            raw = bytes;
        }

        file.Parse(raw);
        return file;
    }

    private void Parse(byte[] raw)
    {
        if (raw.Length < HeaderSize || raw[0] != (byte)'T' || raw[1] != (byte)'C' || raw[2] != (byte)'D' || raw[3] != (byte)'T')
            throw new InvalidDataException("Это не TCDT/Z2HM файл Manhunt 2.");

        uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x08));
        RelocCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x14));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x20));
        uint head = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x24));

        int relocBytes = checked((int)RelocCount * 4);
        if (relocBytes > raw.Length - HeaderSize)
            throw new InvalidDataException("Некорректный размер таблицы релокаций.");

        int payloadLen = raw.Length - relocBytes;
        _payload = raw.AsSpan(0, payloadLen).ToArray();
        _reloc = raw.AsSpan(payloadLen).ToArray();

        if (fileSize != 0 && fileSize != raw.Length)
        {
            // не критично, просто запомним фактический размер
        }

        var seen = new HashSet<uint>();
        uint node = head;
        while (node != SentinelOffset && node + NodeSize <= payloadLen && seen.Add(node))
        {
            var walk = _payload.AsSpan((int)node);
            string name = ReadName(walk);

            uint w = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x48..]);
            uint h = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x4C..]);
            uint bpp = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x50..]);
            uint pitch = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x54..]);
            uint blk = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x58..]);
            ushort mips = BinaryPrimitives.ReadUInt16LittleEndian(walk[0x5C..]);
            uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x60..]);
            byte fmt = walk[0x64];
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(walk[0x68..]);

            if (dataOff + 128 <= payloadLen && dataOff + dataSize <= payloadLen)
            {
                var tex = new TexTexture
                {
                    NodeOffset = (int)node,
                    Name = name,
                    Width = w,
                    Height = h,
                    OriginalWidth = w,
                    OriginalHeight = h,
                    BitsPerPixel = bpp,
                    Pitch = pitch,
                    BlockBytes = blk,
                    MipCount = mips,
                    DataOffset = dataOff,
                    FormatByte = fmt,
                    DataSize = dataSize,
                    OriginalDds = _payload.AsSpan((int)dataOff, (int)dataSize).ToArray()
                };
                tex.Compression = DetectCompression(tex);
                Textures.Add(tex);
            }

            node = BinaryPrimitives.ReadUInt32LittleEndian(walk);
        }

        if (count != Textures.Count)
        {
            // не ошибка: бывает мусор в счётчике, ориентируемся на реальный список
        }
    }

    private static string ReadName(ReadOnlySpan<byte> node)
    {
        var buf = node.Slice(0x08, 0x40);
        int len = buf.IndexOf((byte)0);
        if (len < 0) len = buf.Length;
        return Encoding.ASCII.GetString(buf[..len]);
    }

    public static TexCompression DetectCompression(TexTexture tex)
    {
        var d = tex.OriginalDds;
        if (d.Length >= 128 && d[0] == (byte)'D' && d[1] == (byte)'D' && d[2] == (byte)'S' && d[3] == (byte)' ')
        {
            string fourcc = Encoding.ASCII.GetString(d, 0x54, 4);
            if (fourcc == "DXT1") return TexCompression.Dxt1;
            if (fourcc == "DXT5") return TexCompression.Dxt5;
        }
        return tex.BitsPerPixel >= 16 ? TexCompression.RawArgb : TexCompression.Dxt5;
    }

    // ---------------------------------------------------------------- запись

    public byte[] Build(TexContainerKind kind, Action<string>? log = null)
    {
        if (kind == TexContainerKind.Preserve) kind = SourceKind;

        var data = new List<byte>(_payload.Length + 1024 * 1024);
        data.AddRange(_payload);

        var appended = new List<TexTexture>();

        // 1) те, что влезают на старое место — пишем на месте
        foreach (var tex in Textures)
        {
            if (tex.Replacement == null) continue;
            if (tex.Replacement.Length == tex.DataSize && tex.DataOffset + tex.DataSize <= data.Count)
            {
                for (int i = 0; i < tex.Replacement.Length; i++)
                    data[(int)tex.DataOffset + i] = tex.Replacement[i];
                PatchNode(data, tex, tex.Replacement);
                log?.Invoke($"{tex.Name}: записан на место ({tex.Replacement.Length} байт)");
            }
            else
            {
                appended.Add(tex);
            }
        }

        // 2) остальные — в конец, с выравниванием 16
        foreach (var tex in appended)
        {
            while ((data.Count & 15) != 0) data.Add(0);
            uint offset = (uint)data.Count;
            data.AddRange(tex.Replacement!);
            tex.DataOffset = offset;
            tex.DataSize = (uint)tex.Replacement!.Length;
            PatchNode(data, tex, tex.Replacement!);
            log?.Invoke($"{tex.Name}: добавлен в конец @0x{offset:X} ({tex.Replacement.Length} байт)");
        }

        uint payloadSize = (uint)data.Count;
        uint totalSize = payloadSize + (uint)_reloc.Length;

        WriteU32(data, 0x08, totalSize);
        WriteU32(data, 0x0C, payloadSize);
        WriteU32(data, 0x10, payloadSize);

        data.AddRange(_reloc);

        var raw = data.ToArray();
        if (kind == TexContainerKind.Tcdt)
            return raw;

        using var outMs = new MemoryStream();
        outMs.Write(Encoding.ASCII.GetBytes("Z2HM"));
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)raw.Length);
        outMs.Write(sizeBytes);
        using (var z = new ZLibStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return outMs.ToArray();
    }

    private static void PatchNode(List<byte> data, TexTexture tex, byte[] dds)
    {
        var info = DdsCodec.ReadInfo(dds);
        int n = tex.NodeOffset;
        tex.Width = (uint)info.Width;
        tex.Height = (uint)info.Height;
        tex.MipCount = (ushort)info.MipCount;
        tex.BitsPerPixel = (uint)info.BitsPerPixel;
        tex.Pitch = (uint)Math.Max(16, info.Width * info.BitsPerPixel / 8);
        tex.BlockBytes = (uint)info.BlockBytes;

        WriteU32(data, n + 0x48, (uint)info.Width);
        WriteU32(data, n + 0x4C, (uint)info.Height);
        WriteU32(data, n + 0x50, tex.BitsPerPixel);
        WriteU32(data, n + 0x54, tex.Pitch);
        WriteU32(data, n + 0x58, tex.BlockBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data)[(n + 0x5C)..], (ushort)info.MipCount);
        WriteU32(data, n + 0x60, tex.DataOffset);
        data[n + 0x64] = tex.FormatByte;
        WriteU32(data, n + 0x68, (uint)dds.Length);
    }

    private static void WriteU32(List<byte> data, int offset, uint value)
    {
        var s = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data);
        BinaryPrimitives.WriteUInt32LittleEndian(s[offset..], value);
    }
}
