using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

/// <summary>A standard BMP file, decoded from a WINDEV window's cached preview.</summary>
public sealed class WindevWindowPreview
{
    public int Width { get; }
    public int Height { get; }
    public int StoredHeight { get; }
    public bool IsTopDown => StoredHeight < 0;
    public ushort BitsPerPixel => 32;
    public uint Compression => 3; // BI_BITFIELDS, not compressed at the BMP level.
    public int PixelOffset { get; }
    public int RowStride => Width * 4;
    public uint RedMask => 0x00FF0000;
    public uint GreenMask => 0x0000FF00;
    public uint BlueMask => 0x000000FF;
    public bool HasAlphaMask => false;
    public int BmpByteLength => BmpData.Length;
    [JsonIgnore] public ReadOnlyMemory<byte> BmpData { get; }

    private WindevWindowPreview(ReadOnlyMemory<byte> data, int width, int height, int pixels)
    { BmpData = data; Width = width; StoredHeight = height; Height = Math.Abs(height); PixelOffset = pixels; }

    internal static WindevWindowPreview Parse(ReadOnlyMemory<byte> memory)
    {
        var data = memory.Span; Binary.Require(data, 0, 66);
        if (!data.StartsWith("BM"u8) || Binary.U32(data, 2) != data.Length)
            throw new WindevFormatException("Invalid preview BMP signature or file size");
        if (Binary.U32(data, 14) != 40 || Binary.U16(data, 26) != 1 ||
            Binary.U16(data, 28) != 32 || Binary.U32(data, 30) != 3)
            throw new UnsupportedWindevFormatException("Preview BMP variant requires BITMAPINFOHEADER/32-bit/BI_BITFIELDS");
        if (Binary.U32(data, 54) != 0xFF0000 || Binary.U32(data, 58) != 0xFF00 || Binary.U32(data, 62) != 0xFF)
            throw new UnsupportedWindevFormatException("Unsupported preview BMP color masks");
        int width = unchecked((int)Binary.U32(data, 18)), height = unchecked((int)Binary.U32(data, 22));
        if (width <= 0 || height is 0 or int.MinValue) throw new WindevFormatException("Invalid preview dimensions", 18);
        int pixels = Binary.Offset(Binary.U32(data, 10), data.Length);
        long size = (long)width * Math.Abs(height) * 4;
        if (pixels < 66 || size > data.Length - pixels || Binary.U32(data, 34) != size)
            throw new WindevFormatException("Preview pixel boundaries or size mismatch", 10);
        return new(memory, width, height, pixels);
    }

    /// <summary>Converts bottom-up BGRX pixels to top-down RGBA. Reserved X is not interpreted as alpha.</summary>
    public byte[] GetRgba32()
    {
        var result = new byte[checked(RowStride * Height)]; var source = BmpData.Span;
        for (int y = 0; y < Height; y++)
        {
            int src = PixelOffset + (IsTopDown ? y : Height - 1 - y) * RowStride, dst = y * RowStride;
            for (int x = 0; x < Width; x++, src += 4, dst += 4)
            { result[dst] = source[src + 2]; result[dst + 1] = source[src + 1]; result[dst + 2] = source[src]; result[dst + 3] = 255; }
        }
        return result;
    }

    public void SaveBmp(string filename) => File.WriteAllBytes(filename, BmpData.ToArray());
}
