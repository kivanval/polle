using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Polle.Api.Features.Drought;

/// <summary>
/// Paints per-cell palette codes onto a Web Mercator image of the whole map, for a Leaflet ImageOverlay with
/// bounds ±85.05°. Same sampling as the pipeline's render.py.
/// </summary>
public static class MercatorOverlay
{
    public const int Size = 1440;
    private const int Rows = 360, Columns = 720;
    private static readonly int[] RowOf = [.. Enumerable.Range(0, Size).Select(y =>
    {
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * ((y + 0.5) / Size)))) * 180 / Math.PI;
        return Math.Clamp((int)Math.Floor((90 - latitude) / 0.5), 0, Rows - 1);
    })];
    private static readonly int[] ColumnOf = [.. Enumerable.Range(0, Size).Select(x =>
        Math.Clamp((int)Math.Floor((-180 + (x + 0.5) * 360 / Size + 180) / 0.5), 0, Columns - 1))];

    /// <summary>Row-major pixels; code 0 (transparent) wherever there is no land cell.</summary>
    public static byte[] Paint(int[] cells, byte[] codes)
    {
        var grid = new byte[Rows * Columns];
        for (var i = 0; i < cells.Length; i++) grid[cells[i]] = codes[i];
        var pixels = new byte[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            var source = RowOf[y] * Columns;
            for (var x = 0; x < Size; x++) pixels[y * Size + x] = grid[source + ColumnOf[x]];
        }
        return pixels;
    }
}

/// <summary>Minimal 8-bit indexed PNG writer: index 0 is transparent, index k is colors[k - 1].</summary>
public static class PalettePng
{
    private static readonly uint[] CrcTable = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    })];

    public static byte[] Encode(byte[] pixels, int width, int height, IReadOnlyList<string> colors)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 3;  // indexed colour
        Chunk(png, "IHDR", header);

        var palette = new byte[(colors.Count + 1) * 3];
        for (var i = 0; i < colors.Count; i++)
            for (var channel = 0; channel < 3; channel++)
                palette[(i + 1) * 3 + channel] = byte.Parse(colors[i].AsSpan(1 + channel * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Chunk(png, "PLTE", palette);
        Chunk(png, "tRNS", [0]);  // entries after the first stay opaque

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);  // no filter
                zlib.Write(pixels, y * width, width);
            }
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        var name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in name.Concat(data)) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFFu);
        output.Write(number);
    }
}
