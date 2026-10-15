using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed record WindevLibraryHeader(uint ContainerRevision, ushort WindevVersion, uint ArchiveRevision,
    uint RawFlags, ulong PayloadByteLength, int PropertiesOffset, int PropertiesStoredLength,
    int DirectoryOffset, int DirectoryStoredLength, int ExtraHeaderOffset, int ExtraHeaderLength, int PayloadOffset);

/// <summary>A stored range whose offset is relative to the library payload, not the file.</summary>
public sealed record WindevLibraryChunk(uint RawKind, ulong PayloadRelativeOffset, int StoredByteLength);

public sealed class WindevLibraryEntry
{
    public int Index { get; }
    public int DirectoryOffset { get; }
    public int DirectoryByteLength { get; }
    public string LogicalName { get; }
    public string OriginalPath { get; }
    public uint RawDocumentType { get; }
    public uint RawFlags { get; }
    public bool IsResource => RawDocumentType == 0;
    public bool IsLzwCompressed => (RawFlags & 0x80) != 0;
    public ulong RawIdentifier { get; }
    public IReadOnlyList<WindevLibraryChunk> Chunks { get; }
    public IReadOnlyList<WindevLibraryChunk> AuxiliaryChunks { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> RawTrailingMetadata { get; }
    internal WindevLibraryEntry(int index, int offset, int length, string name, string path, uint kind, uint flags,
        ulong identifier, IReadOnlyList<WindevLibraryChunk> chunks, IReadOnlyList<WindevLibraryChunk> auxiliary,
        ReadOnlyMemory<byte> metadata)
    {
        Index = index; DirectoryOffset = offset; DirectoryByteLength = length; LogicalName = name; OriginalPath = path;
        RawDocumentType = kind; RawFlags = flags; RawIdentifier = identifier;
        Chunks = chunks; AuxiliaryChunks = auxiliary; RawTrailingMetadata = metadata;
    }
}

/// <summary>Independent read-only decoder for the observed 0x0D0001 / revision 3102 library and patch format.</summary>
public sealed class WdlWindevLibrary : IWindevFile
{
    public string FileName { get; }
    public string Name { get; }
    public string? ApplicationVersion { get; }
    public WindevLibraryHeader Header { get; }
    public IReadOnlyList<WindevLibraryEntry> Entries { get; }
    public IReadOnlyList<WindevString> PropertyStrings { get; }
    public IReadOnlyList<WindevDiagnostic> Diagnostics { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> RawData { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> DecodedProperties { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> DecodedDirectory { get; }
    private readonly WindevOpenOptions _options;

    private WdlWindevLibrary(string filename, byte[] data, WindevOpenOptions options)
    {
        FileName = Path.GetFullPath(filename); RawData = data; _options = options;
        uint revision = Binary.U32(data, 20), flags = Binary.U32(data, 24);
        if (revision != 3102) throw new UnsupportedWindevFormatException("Unsupported WDL archive revision (expected 3102)", 20);
        int propertiesLength = Binary.Count(Binary.U32(data, 28), data.Length - 32, 28);
        if (propertiesLength < 2) throw new WindevFormatException("Missing WDL properties block", 28);
        int afterProperties = 32 + propertiesLength; Binary.Require(data, afterProperties, 12);
        ulong payloadLength = Binary.U64(data, afterProperties);
        int directoryLength = Binary.Count(Binary.U32(data, afterProperties + 8), data.Length - afterProperties - 12, afterProperties + 8);
        if (directoryLength < 2) throw new WindevFormatException("Missing WDL directory block", afterProperties + 8);
        int directoryOffset = afterProperties + 12, extraOffset = directoryOffset + directoryLength;
        Binary.Require(data, extraOffset, 4);
        int extraLength = Binary.Count(Binary.U32(data, extraOffset), data.Length - extraOffset - 4, extraOffset);
        int payloadOffset = extraOffset + 4 + extraLength;
        if (payloadLength != (ulong)(data.Length - payloadOffset))
            throw new WindevFormatException("WDL payload length does not cover the file", afterProperties);
        Header = new(20, Binary.U16(data, 14), revision, flags, payloadLength, 32, propertiesLength,
            directoryOffset, directoryLength, extraOffset, extraLength, payloadOffset);
        if (options.MaxDecodedBlocks < 2) throw new WindevFormatException("WDL requires two decoded metadata blocks");
        var properties = DecodeMetadata(data.AsSpan(32, propertiesLength), options, options.MaxTotalDecodedBytes);
        var directory = DecodeMetadata(data.AsSpan(directoryOffset, directoryLength), options, options.MaxTotalDecodedBytes - properties.Length);
        if ((long)properties.Length + directory.Length > options.MaxTotalDecodedBytes)
            throw new WindevFormatException("WDL metadata exceeds total decompression budget");
        DecodedProperties = properties; DecodedDirectory = directory;
        PropertyStrings = BinaryText.FindStrings(properties, options.MaxStringBytes);
        int cursor = 0;
        _ = ReadText(properties, ref cursor, properties.Length, options);
        Name = ReadText(properties, ref cursor, properties.Length, options);
        _ = ReadText(properties, ref cursor, properties.Length, options);
        ApplicationVersion = ReadText(properties, ref cursor, properties.Length, options);
        Binary.Require(directory, 0, 4);
        int count = Binary.Count(Binary.U32(directory, 0), options.MaxArchiveEntries, 0);
        var entries = new List<WindevLibraryEntry>(); cursor = 4;
        for (int i = 0; i < count; i++)
        {
            int start = cursor; Binary.Require(directory, cursor, 4);
            int length = Binary.Count(Binary.U32(directory, cursor), directory.Length - cursor, cursor);
            if (length < 4) throw new WindevFormatException("Invalid WDL entry length", cursor);
            int end = cursor + length; cursor += 4;
            string name = ReadText(directory, ref cursor, end, options), path = ReadText(directory, ref cursor, end, options);
            RequireEntry(cursor, 8, end);
            uint kind = Binary.U32(directory, cursor), entryFlags = Binary.U32(directory, cursor + 4); cursor += 8;
            var chunks = ReadChunks(directory, ref cursor, end, payloadLength, options);
            RequireEntry(cursor, 8, end); ulong identifier = Binary.U64(directory, cursor); cursor += 8;
            var auxiliary = ReadChunks(directory, ref cursor, end, payloadLength, options);
            // The first two ranges in the trailing metadata are retained with neutral names.
            RequireEntry(cursor, 24, end);
            for (int j = 0; j < 2; j++) CheckRange(Binary.U64(directory, cursor + j * 12), Binary.U32(directory, cursor + j * 12 + 8), payloadLength);
            entries.Add(new(i, start, length, name, path, kind, entryFlags, identifier, chunks, auxiliary,
                directory.AsMemory(cursor, end - cursor)));
            cursor = end;
        }
        if (cursor != directory.Length) throw new WindevFormatException("Unconsumed WDL directory bytes", cursor);
        Entries = entries.AsReadOnly();
        var diagnostics = new List<WindevDiagnostic>
        {
            new("PartialCompiledLibrary", DiagnosticSeverity.Warning,
                "Directory and stored chunks are decoded. Compiled objects, bytecode and remaining property fields are not original WDW source files.")
        };
        if (Header.WindevVersion != 31) diagnostics.Add(new("UntestedVersion", DiagnosticSeverity.Warning, "Only WINDEV 31 WDL files were validated."));
        Diagnostics = diagnostics.AsReadOnly();
    }

    public static WdlWindevLibrary Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, FileInput.Read(filename, 0xD0001, options, 32), options); }

    private static byte[] DecodeMetadata(ReadOnlySpan<byte> stored, WindevOpenOptions options, int remainingBytes)
    {
        byte[] clear = PcsLegacyCipher.DecodeLibrary(stored);
        Binary.Require(clear, 0, 11);
        if (Binary.U32(clear, 5) > remainingBytes) throw new WindevFormatException("WDL metadata exceeds total decompression budget");
        byte[] decoded = PcsContainer.DecodeLzwWrapper(clear, 0, options, out int consumed);
        if (consumed != stored.Length) throw new WindevFormatException("WDL metadata envelope length mismatch");
        return decoded;
    }

    private static string ReadText(ReadOnlySpan<byte> data, ref int cursor, int end, WindevOpenOptions options)
    {
        RequireEntry(cursor, 1, end);
        if (data[cursor] == 0) { cursor++; return ""; }
        if (!BinaryText.TryString(data[..end], cursor, options.MaxStringBytes, out var value))
            throw new WindevFormatException("Invalid framed WDL string", cursor);
        cursor += value.ByteLength; return value.Value;
    }

    private static IReadOnlyList<WindevLibraryChunk> ReadChunks(ReadOnlySpan<byte> data, ref int cursor, int end,
        ulong payloadLength, WindevOpenOptions options)
    {
        RequireEntry(cursor, 4, end);
        int count = Binary.Count(Binary.U32(data, cursor), options.MaxChunksPerEntry, cursor); cursor += 4;
        RequireEntry(cursor, checked(count * 16), end);
        var chunks = new List<WindevLibraryChunk>();
        for (int i = 0; i < count; i++, cursor += 16)
        {
            ulong position = Binary.U64(data, cursor + 4); uint length = Binary.U32(data, cursor + 12);
            CheckRange(position, length, payloadLength);
            chunks.Add(new(Binary.U32(data, cursor), position, checked((int)length)));
        }
        return chunks.AsReadOnly();
    }
    private static void RequireEntry(int offset, int length, int end)
    { if (length < 0 || offset < 0 || length > end - offset) throw new WindevFormatException("Truncated WDL entry", offset); }
    private static void CheckRange(ulong position, uint length, ulong payloadLength)
    {
        // Empty optional ranges may carry a sentinel offset.
        if (length != 0 && (position > payloadLength || length > payloadLength - position))
            throw new WindevFormatException("WDL chunk is outside its payload");
    }

    /// <summary>Returns bytes of an owned primary or auxiliary chunk without interpreting compiled data.</summary>
    public ReadOnlyMemory<byte> ReadStoredChunk(WindevLibraryEntry entry, WindevLibraryChunk chunk)
    {
        if (!Entries.Contains(entry) || (!entry.Chunks.Contains(chunk) && !entry.AuxiliaryChunks.Contains(chunk)))
            throw new ArgumentException("The entry/chunk does not belong to this library.");
        if (chunk.StoredByteLength == 0) return ReadOnlyMemory<byte>.Empty;
        int position = checked(Header.PayloadOffset + (int)chunk.PayloadRelativeOffset);
        return RawData.Slice(position, chunk.StoredByteLength);
    }

    /// <summary>Returns a standalone resource, decompressing the observed flag-0x80 LZW representation.</summary>
    public byte[] ReadResource(WindevLibraryEntry entry)
    {
        if (!Entries.Contains(entry)) throw new ArgumentException("The entry does not belong to this library.", nameof(entry));
        if (!entry.IsResource || entry.RawFlags is not (2 or 130) || entry.Chunks.Count != 1 || entry.Chunks[0].RawKind != 0)
            throw new UnsupportedWindevFormatException("Only observed single-chunk resource representations are supported");
        var raw = ReadStoredChunk(entry, entry.Chunks[0]);
        int limit = Math.Min(_options.MaxDecodedBlockBytes, _options.MaxTotalDecodedBytes);
        if (!entry.IsLzwCompressed)
        {
            if (raw.Length > limit) throw new WindevFormatException("Resource exceeds decoded size budget");
            return raw.ToArray();
        }
        Binary.Require(raw.Span, 0, 11);
        if (Binary.U32(raw.Span, 5) > limit) throw new WindevFormatException("Resource exceeds decoded size budget");
        byte[] result = PcsContainer.DecodeLzwWrapper(raw.Span, 0, _options, out int consumed);
        if (consumed != raw.Length) throw new WindevFormatException("WDL resource envelope length mismatch");
        return result;
    }

    /// <summary>Decodes the observed compiled window property chunk (16-byte prefix + LZW), not a complete WDW file.</summary>
    public WindevDecodedBlock ReadWindowProperties(WindevLibraryEntry entry)
    {
        if (!Entries.Contains(entry)) throw new ArgumentException("The entry does not belong to this library.", nameof(entry));
        if (entry.RawDocumentType != 2 || entry.RawFlags != 0 || entry.Chunks.Count != 1 || entry.Chunks[0].RawKind != 1)
            throw new UnsupportedWindevFormatException("Unsupported compiled window property representation");
        var raw = ReadStoredChunk(entry, entry.Chunks[0]); Binary.Require(raw.Span, 16, 11);
        if (Binary.U32(raw.Span, 21) > _options.MaxTotalDecodedBytes) throw new WindevFormatException("Window property chunk exceeds decoded budget");
        byte[] decoded = PcsContainer.DecodeLzwWrapper(raw.Span, 16, _options, out int consumed);
        if (consumed + 16 != raw.Length) throw new WindevFormatException("Compiled window chunk length mismatch");
        Binary.Require(decoded, 0, 8);
        if (Binary.U32(decoded, 4) != 610) throw new UnsupportedWindevFormatException("Unknown compiled window property descriptor");
        return new(16, consumed, "PCS-LZW", decoded, _options);
    }
}
