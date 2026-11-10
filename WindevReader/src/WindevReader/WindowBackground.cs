using WindevReader.Internal;

namespace WindevReader;

/// <summary>An explicit RGB color, independent of platform color types.</summary>
public sealed record WindevRgbColor(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

internal static class WindowBackgroundReader
{
    internal static (uint? RawValue, int? Offset, WindevRgbColor? Color) Read(
        ReadOnlySpan<byte> data,
        ushort version,
        string expectedName,
        int maxStringBytes)
    {
        if (version != 29 || data.Length < 8 || Binary.U32(data, 4) != 607)
        {
            return (null, null, null);
        }

        if (!BinaryText.TryString(data, 8, maxStringBytes, out WindevString name) || name.Value != expectedName)
        {
            return (null, null, null);
        }

        int nameEnd = name.Offset + name.ByteLength;

        if (data.Length - nameEnd < 85)
        {
            return (null, null, null);
        }

        int colorOffset = nameEnd + 81;
        uint rawValue = Binary.U32(data, colorOffset);
        WindevRgbColor? color = null;

        // Only explicit 24-bit colors are established; system and special values stay raw.
        if (rawValue <= 0x00FFFFFF)
        {
            color = new WindevRgbColor((byte)rawValue, (byte)(rawValue >> 8), (byte)(rawValue >> 16));
        }

        return (rawValue, colorOffset, color);
    }
}
