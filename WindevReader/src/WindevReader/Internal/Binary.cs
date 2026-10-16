using System.Buffers.Binary;
using System.Text;

namespace WindevReader.Internal;
internal static class Binary
{
    public static void Require(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new WindevFormatException("Truncated or out-of-bounds binary record", offset);
        }
    }

    public static ushort U16(ReadOnlySpan<byte> data, int offset)
    {
        Require(data, offset, 2);

        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    public static uint U32(ReadOnlySpan<byte> data, int offset)
    {
        Require(data, offset, 4);

        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    public static ulong U64(ReadOnlySpan<byte> data, int offset)
    {
        Require(data, offset, 8);

        return BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    }

    public static int Offset(ulong value, int length)
    {
        if (value > (ulong)length)
        {
            throw new WindevFormatException("Invalid record pointer");
        }

        return (int)value;
    }

    public static int Count(uint value, int max, int offset)
    {
        if (value > max)
        {
            throw new WindevFormatException("Record count exceeds reader limit", offset);
        }

        return (int)value;
    }
}

internal static class BinaryText
{
    private static readonly UTF8Encoding utf8 = new(false, true);

    private static readonly byte[] startMarker = [0xff, 0xce, 0x88, 0xf1];

    private static readonly byte[] endMarker = [0xfe, 0xed, 0x98, 0xe1];

    private const string cp1252 = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeCp1252(bytes);
        }
    }

    internal static string DecodeCp1252(ReadOnlySpan<byte> bytes)
    {
        char[] characters = new char[bytes.Length];

        for (int index = 0; index < bytes.Length; index++)
        {
            characters[index] = bytes[index] is >= 128 and < 160
                ? cp1252[bytes[index] - 128]
                : (char)bytes[index];
        }

        return new string(characters);
    }

    internal static IReadOnlyList<WindevString> FindZeroTerminatedCandidates(ReadOnlySpan<byte> data, int maxBytes)
    {
        List<WindevString> candidates = new List<WindevString>();
        int stringOffset = 0;

        for (int cursor = 0; cursor < data.Length; cursor++)
        {
            byte value = data[cursor];

            if (value == 0)
            {
                int length = cursor - stringOffset;

                if (length >= 3 && length <= maxBytes)
                {
                    candidates.Add(new WindevString(
                        stringOffset, length + 1, DecodeCp1252(data.Slice(stringOffset, length))));
                }

                stringOffset = cursor + 1;
            }
            else if (value < 32 || value == 127)
            {
                stringOffset = cursor + 1;
            }
        }

        return candidates.AsReadOnly();
    }

    public static bool TryString(ReadOnlySpan<byte> data, int offset, int maxBytes, out WindevString result)
    {
        result = null!;

        if (offset < 0 || offset > data.Length - 13 || !data.Slice(offset, 4).SequenceEqual(startMarker))
        {
            return false;
        }

        uint length = Binary.U32(data, offset + 4);

        if (length < 13 || length > maxBytes || length > data.Length - offset)
        {
            return false;
        }

        int framedByteLength = (int)length;

        if (data[offset + framedByteLength - 5] != 0 || !data.Slice(offset + framedByteLength - 4, 4).SequenceEqual(endMarker))
        {
            return false;
        }

        ReadOnlySpan<byte> text = data.Slice(offset + 8, framedByteLength - 13);

        if (text.Contains((byte)0))
        {
            return false;
        }

        result = new WindevString(offset, framedByteLength, Decode(text));

        return true;
    }

    public static IReadOnlyList<WindevString> FindStrings(ReadOnlySpan<byte> data, int maxBytes)
    {
        List<WindevString> result = new List<WindevString>();

        for (int cursor = 0; cursor <= data.Length - 13; cursor++)
        {
            if (TryString(data, cursor, maxBytes, out WindevString value))
            {
                result.Add(value);

                cursor += value.ByteLength - 1;
            }
        }

        return result.AsReadOnly();
    }

    public static string CString(ReadOnlySpan<byte> data, ref int offset, int maxBytes, bool useCp1252 = false)
    {
        Binary.Require(data, offset, 1);

        int length = data[offset..].IndexOf((byte)0);

        if (length < 0 || length > maxBytes)
        {
            throw new WindevFormatException("Invalid zero-terminated string", offset);
        }

        string value = useCp1252
            ? DecodeCp1252(data.Slice(offset, length))
            : Decode(data.Slice(offset, length));

        offset += length + 1;

        return value;
    }
}
