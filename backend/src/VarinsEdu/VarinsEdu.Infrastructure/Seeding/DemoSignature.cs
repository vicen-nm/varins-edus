using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace VarinsEdu.Infrastructure.Seeding;

// Draws a scribble that looks like a signature and encodes it as a small PNG, with no image library.
internal static class DemoSignature
{
    private const int Width = 300;
    private const int Height = 100;

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Create(int seed)
    {
        var pixels = new byte[Width * Height];
        Array.Fill(pixels, (byte)255);

        var random = new Random(seed);
        var amplitude = 14 + random.Next(10);
        var frequency = 0.05 + random.NextDouble() * 0.04;
        var phase = random.NextDouble() * Math.PI;

        for (var x = 20; x < 260; x++)
        {
            var y = 50 + (int)(amplitude * Math.Sin(x * frequency + phase)) + (int)(8 * Math.Sin(x * 0.19));
            Ink(pixels, x, y);
        }

        // A slightly tilted underline.
        for (var x = 40; x < 240; x++)
        {
            Ink(pixels, x, 80 + (x / 40) % 3);
        }

        return Encode(pixels);
    }

    private static void Ink(byte[] pixels, int x, int y)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var px = x + dx;
                var py = y + dy;

                if (px >= 0 && px < Width && py >= 0 && py < Height)
                {
                    pixels[py * Width + px] = 0;
                }
            }
        }
    }

    // 8-bit grayscale, every scanline with filter 0.
    private static byte[] Encode(byte[] pixels)
    {
        using var raw = new MemoryStream();

        for (var row = 0; row < Height; row++)
        {
            raw.WriteByte(0);
            raw.Write(pixels, row * Width, Width);
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), Height);
        header[8] = 8; // bit depth
        header[9] = 0; // grayscale

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var number = new byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        stream.Write(typeBytes);
        stream.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeBytes, data));
        stream.Write(number);
    }

    private static uint Crc32(params byte[][] parts)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var part in parts)
        {
            foreach (var value in part)
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
