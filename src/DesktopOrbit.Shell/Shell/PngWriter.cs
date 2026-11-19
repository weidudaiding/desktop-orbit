using System.IO.Compression;

namespace DesktopOrbit.Shell.Shell;

/// <summary>
/// Minimal PNG encoder for 32bpp premultiplied BGRA buffers. Avoids a dependency
/// on System.Drawing.Common just to turn an HBITMAP into an icon file.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] EncodeArgb32(byte[] premultipliedBgra, int width, int height)
    {
        byte[] raw = BuildScanlines(premultipliedBgra, width, height);
        byte[] compressed = ZlibCompress(raw);

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);

        byte[] ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] BuildScanlines(byte[] bgra, int width, int height)
    {
        int stride = width * 4;
        byte[] raw = new byte[(stride + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int targetRow = y * (stride + 1);
            raw[targetRow] = 0;

            for (int x = 0; x < width; x++)
            {
                int s = sourceRow + x * 4;
                int t = targetRow + 1 + x * 4;

                byte b = bgra[s];
                byte g = bgra[s + 1];
                byte r = bgra[s + 2];
                byte a = bgra[s + 3];

                if (a is > 0 and < 255)
                {
                    r = Unpremultiply(r, a);
                    g = Unpremultiply(g, a);
                    b = Unpremultiply(b, a);
                }
                else if (a == 0)
                {
                    r = g = b = 0;
                }

                raw[t] = r;
                raw[t + 1] = g;
                raw[t + 2] = b;
                raw[t + 3] = a;
            }
        }

        return raw;
    }

    private static byte Unpremultiply(byte value, byte alpha)
    {
        int result = value * 255 / alpha;
        return (byte)Math.Min(255, result);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint adler = Adler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length, 0, 4);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes, 0, 4);
        if (data.Length > 0) output.Write(data, 0, data.Length);

        uint crc = UpdateCrc(0xFFFFFFFF, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFF;

        byte[] crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes, 0, 4);
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
