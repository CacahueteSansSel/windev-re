using System.Text;

namespace WindevReader.Internal;
internal sealed record PcsContainer(byte[] RawData, WindevHeader Header,
    IReadOnlyList<WindevSection> Sections, List<WindevDiagnostic> Diagnostics)
{
    public static PcsContainer Read(string filename, WindevOpenOptions options)
    {
        options.Validate();
        using var stream = File.OpenRead(filename);
        if (stream.Length > options.MaxFileBytes) throw new WindevFormatException("File exceeds reader limit");
        var data = new byte[(int)stream.Length]; stream.ReadExactly(data);
        return Parse(data, options);
    }
    public static PcsContainer Parse(byte[] data, WindevOpenOptions options)
    {
        options.Validate(); Binary.Require(data, 0, 85);
        if (!data.AsSpan(0, 4).SequenceEqual("PCS\0"u8)) throw new WindevFormatException("Not a PCS document", 0);
        if (Binary.U32(data, 4) != 20) throw new UnsupportedWindevFormatException("Unsupported PCS container revision", 4);
        ushort version = Binary.U16(data, 14);
        if (version is not (28 or 31) && !options.AllowUntestedVersions)
            throw new UnsupportedWindevFormatException($"WINDEV {version} was not validated; opt in with AllowUntestedVersions", 14);
        int count = Binary.Count(Binary.U32(data, 81), 256, 81);
        if (count == 0) throw new WindevFormatException("PCS section directory is empty", 81);
        int tableEnd = 85 + count * 5; Binary.Require(data, 85, count * 5);
        ulong payloadValue = 32UL + Binary.U32(data, 32);
        int payload = Binary.Offset(payloadValue, data.Length);
        if (payload < tableEnd) throw new WindevFormatException("PCS payload overlaps its section directory", 32);
        long expectedEnd = payload;
        for (int i = 0; i < count; i++) expectedEnd += Binary.U32(data, 86 + i * 5);
        if (expectedEnd != data.Length) throw new WindevFormatException("PCS section lengths do not cover the file exactly", 85);
        var diagnostics = new List<WindevDiagnostic>();
        if (version is not (28 or 31)) diagnostics.Add(new("UntestedVersion", DiagnosticSeverity.Warning, $"WINDEV {version} is untested."));
        var sections = new List<WindevSection>(); int offset = payload, totalDecoded = 0, blockCount = 0;
        uint kind = Binary.U32(data, 8);
        for (int i = 0; i < count; i++)
        {
            byte tag = data[85 + i * 5]; int length = (int)Binary.U32(data, 86 + i * 5);
            ReadOnlyMemory<byte> raw = data.AsMemory(offset, length);
            var blocks = new List<WindevDecodedBlock>();
            if ((kind & 0xffff) == 4 && i == 1 && length != 0)
            {
                byte[] decrypted = PcsLegacyCipher.DecodeAnalysis(raw.Span);
                byte[] decoded = DecodeLzwWrapper(decrypted, 4, options, out int storedLength);
                byte[] full = new byte[decoded.Length + 4]; decrypted.AsSpan(0, 4).CopyTo(full); decoded.CopyTo(full, 4);
                CheckBudget(full.Length, options, ref totalDecoded, ref blockCount);
                blocks.Add(new(0, storedLength + 4, "PCS-legacy + LZW", full, options));
            }
            else
            {
                for (int p = 0; p <= length - 11; p++)
                {
                    if (raw.Span[p] != 4 || raw.Span[p + 9] != 0 || raw.Span[p + 10] != 0) continue;
                    uint encoded = Binary.U32(raw.Span, p + 1), decodedSize = Binary.U32(raw.Span, p + 5);
                    if (encoded < 2 || encoded > length - p - 11 || decodedSize > options.MaxDecodedBlockBytes) continue;
                    byte[] decoded;
                    try { decoded = DecodeLzwWrapper(raw.Span, p, options, out _); }
                    catch (WindevFormatException) { continue; } // Candidate discovery in opaque property/image buffers.
                    CheckBudget(decoded.Length, options, ref totalDecoded, ref blockCount);
                    int storedLength = 11 + (int)encoded;
                    blocks.Add(new(p, storedLength, "PCS-LZW", decoded, options)); p += storedLength - 1;
                }
                // The executable code section always carries a direct, four-byte-prefixed LZW envelope in this corpus.
                if (i == 2 && tag == 2 && length > 0 && blocks.All(b => b.Offset != 4))
                    throw new WindevFormatException("Code section could not be decoded", offset + 4);
            }
            sections.Add(new(i, tag, offset, raw, blocks.AsReadOnly(), options)); offset += length;
        }
        return new(data, new(20, kind, version, Binary.U16(data, 12), Encoding.ASCII.GetString(data, 70, 10), payload, count),
            sections.AsReadOnly(), diagnostics);
    }
    public static byte[] DecodeLzwWrapper(ReadOnlySpan<byte> data, int offset, WindevOpenOptions options, out int storedLength)
    {
        Binary.Require(data, offset, 11);
        if (data[offset] != 4 || data[offset + 9] != 0 || data[offset + 10] != 0)
            throw new UnsupportedWindevFormatException("Unsupported compression envelope", offset);
        int length = Binary.Count(Binary.U32(data, offset + 1), data.Length, offset + 1);
        int expected = Binary.Count(Binary.U32(data, offset + 5), options.MaxDecodedBlockBytes, offset + 5);
        Binary.Require(data, offset + 11, length); storedLength = length + 11;
        return PcsLzw.Decode(data.Slice(offset + 11, length), expected, options.MaxDecodedBlockBytes);
    }
    private static void CheckBudget(int length, WindevOpenOptions options, ref int total, ref int count)
    {
        if (++count > options.MaxDecodedBlocks || length > options.MaxTotalDecodedBytes - total)
            throw new WindevFormatException("Total decompression budget exceeded");
        total += length;
    }
}
