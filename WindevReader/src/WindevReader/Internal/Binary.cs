using System.Buffers.Binary;
using System.Text;

namespace WindevReader.Internal;
internal static class Binary
{
    public static void Require(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new WindevFormatException("Truncated or out-of-bounds binary record", offset);
    }
    public static ushort U16(ReadOnlySpan<byte> data, int offset)
    { Require(data, offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]); }
    public static uint U32(ReadOnlySpan<byte> data, int offset)
    { Require(data, offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); }
    public static ulong U64(ReadOnlySpan<byte> data, int offset)
    { Require(data, offset, 8); return BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]); }
    public static int Offset(ulong value, int length)
    {
        if (value > (ulong)length) throw new WindevFormatException("Invalid record pointer");
        return (int)value;
    }
    public static int Count(uint value, int max, int offset)
    {
        if (value > max) throw new WindevFormatException("Record count exceeds reader limit", offset);
        return (int)value;
    }
}
internal static class BinaryText
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly byte[] Start = [0xff, 0xce, 0x88, 0xf1];
    private static readonly byte[] End = [0xfe, 0xed, 0x98, 0xe1];
    private const string Cp1252 = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] is >= 128 and < 160 ? Cp1252[bytes[i] - 128] : (char)bytes[i];
            return new string(chars);
        }
    }
    public static bool TryString(ReadOnlySpan<byte> data, int offset, int maxBytes, out WindevString result)
    {
        result = null!;
        if (offset < 0 || offset > data.Length - 13 || !data.Slice(offset, 4).SequenceEqual(Start)) return false;
        uint length = Binary.U32(data, offset + 4);
        if (length < 13 || length > maxBytes || length > data.Length - offset) return false;
        int n = (int)length;
        if (data[offset + n - 5] != 0 || !data.Slice(offset + n - 4, 4).SequenceEqual(End)) return false;
        var text = data.Slice(offset + 8, n - 13);
        if (text.Contains((byte)0)) return false;
        result = new(offset, n, Decode(text)); return true;
    }
    public static IReadOnlyList<WindevString> FindStrings(ReadOnlySpan<byte> data, int maxBytes)
    {
        var result = new List<WindevString>();
        for (int p = 0; p <= data.Length - 13; p++)
            if (TryString(data, p, maxBytes, out var value)) { result.Add(value); p += value.ByteLength - 1; }
        return result.AsReadOnly();
    }
    public static string CString(ReadOnlySpan<byte> data, ref int offset, int maxBytes)
    {
        Binary.Require(data, offset, 1);
        int n = data[offset..].IndexOf((byte)0);
        if (n < 0 || n > maxBytes) throw new WindevFormatException("Invalid zero-terminated string", offset);
        string value = Decode(data.Slice(offset, n)); offset += n + 1; return value;
    }
}
