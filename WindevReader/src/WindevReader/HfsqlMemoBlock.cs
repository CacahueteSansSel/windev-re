using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;
public enum HfsqlMemoTextEncoding
{
    Cp1252,
    Utf16Le
}

/// <summary>Observed single-block, mode-0 MMO payload. Allocation chains and raw control words remain uninterpreted.</summary>
public sealed class HfsqlMemoBlock
{
    public int Offset { get; }

    public int PayloadOffset => Offset + 40;

    public int PayloadByteLength => Data.Length;

    public uint StoredRegionByteLength { get; }

    public ushort RawWordAt0 { get; }

    public ushort RawWordAt19 { get; }

    public uint RawWordAt21 { get; }

    public ulong RawWordAt25 { get; }

    public ushort RawWordAt33 { get; }

    public byte RawByteAt35 { get; }

    [JsonIgnore]
    public ReadOnlyMemory<byte> Data { get; }

    private readonly int maxTextBytes;

    private HfsqlMemoBlock(ReadOnlyMemory<byte> data, int offset, int length, WindevOpenOptions options)
    {
        Offset = offset;

        ReadOnlySpan<byte> bytes = data.Span;

        StoredRegionByteLength = Binary.U32(bytes, offset + 2);

        RawWordAt0 = Binary.U16(bytes, offset);

        RawWordAt19 = Binary.U16(bytes, offset + 19);

        RawWordAt21 = Binary.U32(bytes, offset + 21);

        RawWordAt25 = Binary.U64(bytes, offset + 25);

        RawWordAt33 = Binary.U16(bytes, offset + 33);

        RawByteAt35 = bytes[offset + 35];

        Data = data.Slice(offset + 40, length);

        maxTextBytes = options.MaxStringBytes;
    }

    internal static HfsqlMemoBlock Read(ReadOnlyMemory<byte> memory, ulong reference, WindevOpenOptions options)
    {
        int offset = Binary.Offset(reference, memory.Length);
        ReadOnlySpan<byte> bytes = memory.Span;

        if (offset < 210)
        {
            throw new WindevFormatException("Memo reference points inside physical header", offset);
        }

        Binary.Require(bytes, offset, 40);

        if (bytes[offset + 10] != 0 || Binary.U64(bytes, offset + 11) != ulong.MaxValue)
        {
            throw new UnsupportedWindevFormatException("Only observed uncompressed, unchained MMO blocks are supported", offset + 10);
        }

        uint regionLength = Binary.U32(bytes, offset + 2);
        uint copy = Binary.U32(bytes, offset + 6);
        int length = Binary.Count(Binary.U32(bytes, offset + 36), options.MaxDecodedBlockBytes, offset + 36);

        if (regionLength != copy || regionLength != 19UL + (ulong)length)
        {
            throw new WindevFormatException("Memo payload lengths do not match observed profile", offset + 2);
        }

        Binary.Require(bytes, offset + 40, length);

        return new HfsqlMemoBlock(memory, offset, length, options);
    }

    /// <summary>Explicit encoding applied to exactly the declared payload length. No terminator is assumed or removed.</summary>
    public string DecodeText(HfsqlMemoTextEncoding encoding)
    {
        ReadOnlySpan<byte> bytes = Data.Span;

        if (bytes.Length > maxTextBytes)
        {
            throw new WindevFormatException("Memo text exceeds string budget", PayloadOffset);
        }

        switch (encoding)
        {
            case HfsqlMemoTextEncoding.Cp1252:
                const string high = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
                char[] characters = new char[bytes.Length];
                for (int index = 0; index < characters.Length; index++)
                {
                    characters[index] = bytes[index] is >= 128 and < 160 ? high[bytes[index] - 128] : (char)bytes[index];
                }

                return new string(characters);
            case HfsqlMemoTextEncoding.Utf16Le:
                if (bytes.Length % 2 != 0)
                {
                    throw new WindevFormatException("Memo UTF-16LE width invalid", PayloadOffset);
                }

                try
                {
                    return new UnicodeEncoding(false, false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    throw new WindevFormatException("Invalid memo UTF-16LE", PayloadOffset);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }
}
