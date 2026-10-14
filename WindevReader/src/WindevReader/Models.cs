using System.Text.Json.Serialization;

namespace WindevReader;

public enum WindevDocumentKind { Project, Window, GlobalProcedures, Analysis, AnalysisSource, Report }
public enum DiagnosticSeverity { Information, Warning }
public sealed record WindevDiagnostic(string Code, DiagnosticSeverity Severity, string Message, int? SectionIndex = null);
public sealed record WindevHeader(uint ContainerRevision, uint RawDocumentType, ushort WindevVersion,
    ushort ProductRevision, string ProductBuild, int PayloadOffset, int SectionCount);
public sealed record WindevString(int Offset, int ByteLength, string Value);
public sealed record WindevCodeLine(int Offset, string Text, string Author, uint TimestampRaw);
public sealed record WindevCodeBlock(string OwnerName, ushort EventId, ushort LanguageId,
    int Offset, int ByteLength, IReadOnlyList<WindevCodeLine> Lines)
{
    public string Source => string.Join("\r\n", Lines.Select(l => l.Text));
}
public sealed record WindevProcedure(string Name, IReadOnlyList<WindevCodeBlock> CodeBlocks)
{
    public string Source => string.Join("\r\n", CodeBlocks.Where(b => b.Lines.Count != 0).Select(b => b.Source));
}
public sealed record WindevProjectReference(string Name, int Offset, string ConfigurationName);
public sealed record WindevBounds(int X, int Y, int Width, int Height);
/// <summary>An identifier found in an object property buffer. Kind is inferred from its naming prefix.</summary>
public sealed record WindevUiElement(string Name, string InferredKind, int BufferOffset, int Offset, IReadOnlyList<WindevString> NearbyStrings);
public sealed record WindevDataField(uint Id, string Name, string Caption, string Guid, string InputMask,
    ushort RawTypeCode, uint RawFlags, ushort Size, ushort StorageSize, int Offset)
{
    /// <summary>Friendly type for codes observed in the supplied schema; unknown codes remain explicit.</summary>
    public string Type => RawTypeCode switch
    {
        2 => "Text", 11 => "Time", 14 => "Date", 24 => "AutomaticIdentifier64",
        25 => "Int64", 26 => "UInt32", 27 => "UInt64", 28 => "Binary",
        29 => "TextMemo", 34 => "DateTime", 36 => "UInt8", 37 => "Boolean", _ => "Unknown"
    };
}
public sealed record WindevDataTable(uint Id, string Name, string PhysicalName, string Directory,
    string Caption, string Extension, string Guid, int Offset, IReadOnlyList<WindevDataField> Fields);
public sealed record WindevSchemaDirectoryEntry(string Name, long Offset, uint Count);

public sealed class WindevOpenOptions
{
    public int MaxFileBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxDecodedBlockBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxTotalDecodedBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxDecodedBlocks { get; init; } = 4096;
    public int MaxStringBytes { get; init; } = 4 * 1024 * 1024;
    public bool AllowUntestedVersions { get; init; }
    internal void Validate()
    {
        if (MaxFileBytes < 85 || MaxDecodedBlockBytes < 1 || MaxTotalDecodedBytes < 1 ||
            MaxDecodedBlocks < 1 || MaxStringBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(WindevOpenOptions), "All reader limits must be positive.");
    }
}

public class WindevFormatException : IOException
{
    public int? Offset { get; }
    public WindevFormatException(string message, int? offset = null) : base(
        offset.HasValue ? $"{message} (offset 0x{offset:X})" : message) => Offset = offset;
}
public sealed class UnsupportedWindevFormatException : WindevFormatException
{
    public UnsupportedWindevFormatException(string message, int? offset = null) : base(message, offset) { }
}

public sealed class WindevDecodedBlock
{
    public int Offset { get; }
    public int StoredByteLength { get; }
    public string Codec { get; }
    public int DecodedByteLength => Data.Length;
    [JsonIgnore] public ReadOnlyMemory<byte> Data { get; }
    public IReadOnlyList<WindevString> Strings { get; }
    internal WindevDecodedBlock(int offset, int length, string codec, byte[] data, WindevOpenOptions options)
    {
        Offset = offset; StoredByteLength = length; Codec = codec; Data = data;
        Strings = Internal.BinaryText.FindStrings(data, options.MaxStringBytes);
    }
}
public sealed class WindevSection
{
    public int Index { get; }
    public byte RawTag { get; }
    public int Offset { get; }
    public int ByteLength => RawData.Length;
    [JsonIgnore] public ReadOnlyMemory<byte> RawData { get; }
    public IReadOnlyList<WindevString> Strings { get; }
    public IReadOnlyList<WindevDecodedBlock> DecodedBlocks { get; }
    internal WindevSection(int index, byte tag, int offset, ReadOnlyMemory<byte> data,
        IReadOnlyList<WindevDecodedBlock> decoded, WindevOpenOptions options)
    {
        Index = index; RawTag = tag; Offset = offset; RawData = data; DecodedBlocks = decoded;
        Strings = Internal.BinaryText.FindStrings(data.Span, options.MaxStringBytes);
    }
    public uint ReadUInt32(int offset) => Internal.Binary.U32(RawData.Span, offset);
}
