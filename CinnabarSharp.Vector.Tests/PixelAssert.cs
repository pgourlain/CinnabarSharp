using System.IO.Compression;
using System.Security.Cryptography;

namespace CinnabarSharp.Vector.Tests;

/// <summary>Checksum comparison of straight-alpha BGRA buffers, like BlendOpsTests; a failure writes the PNG next to the tests.</summary>
public static class PixelAssert
{
    public static string Checksum(ReadOnlySpan<byte> bgra) => Convert.ToHexString(SHA256.HashData(bgra));

    public static string OutputFolder
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "render-output");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Always writes the PNG (for a human look); fails when the checksum differs from <paramref name="expected"/>.</summary>
    public static void MatchesChecksum(string name, byte[] bgra, int width, int height, string expected)
    {
        var actual = Checksum(bgra);
        var path = WritePng(name, bgra, width, height);
        Assert.True(actual == expected, $"Checksum of '{name}' changed.\n expected {expected}\n actual   {actual}\n image: {path}");
    }

    public static string WritePng(string name, byte[] bgra, int width, int height)
    {
        var path = Path.Combine(OutputFolder, name + ".png");
        File.WriteAllBytes(path, EncodePng(bgra, width, height));
        return path;
    }

    public static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var o = y * (width * 4 + 1);
            raw[o] = 0;
            for (var x = 0; x < width; x++)
            {
                var s = (y * width + x) * 4;
                raw[o + 1 + x * 4] = bgra[s + 2];
                raw[o + 2 + x * 4] = bgra[s + 1];
                raw[o + 3 + x * 4] = bgra[s];
                raw[o + 4 + x * 4] = bgra[s + 3];
            }
        }
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)width);
        WriteBe(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) zs.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    public static (byte B, byte G, byte R, byte A) PixelAt(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var arr in new[] { a, b })
            foreach (var x in arr)
            {
                crc ^= x;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        return ~crc;
    }

    private static void WriteBe(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        var c = new byte[4];
        WriteBe(c, 0, crc);
        s.Write(c);
    }
}
