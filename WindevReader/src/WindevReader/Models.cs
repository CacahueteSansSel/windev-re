using System.Text.Json.Serialization;

namespace WindevReader;
public enum WindevDocumentKind
{
    Project,
    Window,
    GlobalProcedures,
    Analysis,
    AnalysisSource,
    Report
}

public enum DiagnosticSeverity
{
    Information,
    Warning
}

public sealed record WindevDiagnostic(string Code, DiagnosticSeverity Severity, string Message, int? SectionIndex = null);
public sealed record WindevHeader(
    uint ContainerRevision,
    uint RawDocumentType,
    ushort WindevVersion,
    ushort ProductRevision,
    string ProductBuild,
    int PayloadOffset,
    int SectionCount);
public sealed record WindevString(int Offset, int ByteLength, string Value);
public sealed record WindevCodeLine(int Offset, string Text, string Author, uint TimestampRaw);
public sealed record WindevCodeBlock(
    string OwnerName,
    ushort EventId,
    ushort LanguageId,
    int Offset,
    int ByteLength,
    IReadOnlyList<WindevCodeLine> Lines)
{
    public string Source => string.Join("\r\n", Lines.Select(l => l.Text));
}

public sealed record WindevProcedure(string Name, IReadOnlyList<WindevCodeBlock> CodeBlocks)
{
    public string Source => string.Join("\r\n", CodeBlocks.Where(codeBlock => codeBlock.Lines.Count != 0).Select(codeBlock => codeBlock.Source));
}

public sealed record WindevProjectReference(string Name, int Offset, string ConfigurationName);
public sealed record WindevBounds(int X, int Y, int Width, int Height);
/// <summary>An object-property identifier. InferredKind uses its prefix; ControlType uses a recognized WDW record.</summary>
public sealed record WindevUiElement(
    string Name,
    string InferredKind,
    int BufferOffset,
    int Offset,
    IReadOnlyList<WindevString> NearbyStrings)
{
    /// <summary>Bounded serialized record, independent of the naming prefix; null when its envelope is unrecognized.</summary>
    public WindevUiControlRecord? ControlRecord { get; init; }

    public WindevControlType? ControlType => ControlRecord?.Type;

    /// <summary>Serialized design coordinates when a recognized control descriptor is present.</summary>
    public WindevBounds? Bounds { get; init; }

    /// <summary>Caption read at a checked field position; null means the caption profile is not recognized.</summary>
    public string? Caption { get; init; }

    /// <summary>Decoded-buffer positions and uninterpreted numeric values for the recognized layout.</summary>
    public WindevUiLayout? Layout { get; init; }

    /// <summary>Serialized interaction settings for recognized version 28, 29 and 31 layouts.</summary>
    public WindevUiInteraction? Interaction { get; init; }

    /// <summary>Initial visibility stored in the document, independent of later runtime changes.</summary>
    public bool? IsVisible => Interaction?.IsVisible;

    /// <summary>Initial interaction state. Unknown serialized values remain null.</summary>
    public WindevControlState? State => Interaction?.State;

    /// <summary>Stored Tab-access setting, independent of visibility and interaction state.</summary>
    public bool? IsTabStop => Interaction?.IsTabStop;
}

/// <summary>Numeric view of raw bytes. No visibility, state, opacity or navigation meaning is implied.</summary>
public sealed record WindevUiRawValue(int Offset, int ByteLength, ulong Value);

/// <summary>All offsets refer to the element's decoded property buffer, not the compressed file.</summary>
public sealed record WindevUiLayout(
    int DescriptorOffset,
    uint RawDescriptor,
    int IdentifierOffset,
    ulong RawIdentifier,
    int BoundsOffset,
    int? CaptionOffset,
    int? CommonTailOffset,
    IReadOnlyList<WindevUiRawValue> RawValues);
public sealed record WindevDataField(
    uint Id,
    string Name,
    string Caption,
    string Guid,
    string InputMask,
    ushort RawTypeCode,
    uint RawFlags,
    ushort Size,
    ushort StorageSize,
    int Offset)
{
    /// <summary>Raw byte at +31 after the four field CStrings; value 1 verified against MOISJOUR in version 31.</summary>
    public byte RawCompositeMarker
    {
        get; init;
    }
    /// <summary>Observed type-28 composite profile; ordinary type-28 binary fields retain marker 0.</summary>
    public bool IsCompositeKey => RawTypeCode == 28 && RawCompositeMarker == 1;
    /// <summary>Key metadata for the validated WDD 28 and 31 layouts; null for other versions.</summary>
    public WindevFieldKey? Key { get; init; }

    public WindevKeyKind? KeyKind => Key?.Kind;

    public bool? IsPrimaryKey => Key?.IsPrimaryKey;

    public bool? AllowsDuplicates => Key?.AllowsDuplicates;

    public IReadOnlyList<WindevKeyComponent> KeyComponents => Key?.Components ?? Array.Empty<WindevKeyComponent>();

    /// <summary>Friendly type for codes observed in the supplied schema; unknown codes remain explicit.</summary>
    public string Type => RawTypeCode switch
    {
        2 => "Text",
        5 => "Int32",
        11 => "Time",
        14 => "Date",
        24 => "AutomaticIdentifier64",
        25 => "Int64",
        26 => "UInt32",
        27 => "UInt64",
        28 when IsCompositeKey => "CompositeKey",
        28 => "Binary",
        29 => "TextMemo",
        34 => "DateTime",
        36 => "UInt8",
        37 => "Boolean",
        38 => "AutomaticIdentifier32",
        _ => "Unknown"
    };
}

public sealed record WindevDataTable(
    uint Id,
    string Name,
    string PhysicalName,
    string Directory,
    string Caption,
    string Extension,
    string Guid,
    int Offset,
    IReadOnlyList<WindevDataField> Fields)
{
    /// <summary>Secondary CString before the field directories; its exact role is unconfirmed.</summary>
    public string? RawRecordName
    {
        get; init;
    }

    /// <summary>Byte offset of RawRecordName in DecodedSchema, including its four-byte prefix.</summary>
    public int? RawRecordNameOffset
    {
        get; init;
    }

    /// <summary>Raw u32 at +12 after the table's six strings. Verified against the supplied fixed-record profiles.</summary>
    public uint RawRecordByteLength
    {
        get; init;
    }
}

/// <summary>WINDEV 28 relation names and identifiers; cardinalities and constraint flags remain raw.</summary>
public sealed record WindevDataRelation(
    ulong Id,
    string SourceTable,
    ulong SourceTableId,
    string TargetTable,
    ulong TargetTableId,
    string SourceField,
    ulong SourceFieldId,
    string TargetField,
    ulong TargetFieldId,
    string Name,
    IReadOnlyList<ushort> RawFlags,
    uint RawTrailer,
    int Offset);
public sealed record WindevSchemaDirectoryEntry(string Name, long Offset, uint Count);
public sealed class WindevOpenOptions
{
    public int MaxFileBytes { get; init; } = 128 * 1024 * 1024;

    public int MaxDecodedBlockBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxTotalDecodedBytes { get; init; } = 256 * 1024 * 1024;

    public int MaxDecodedBlocks { get; init; } = 4096;

    public int MaxStringBytes { get; init; } = 4 * 1024 * 1024;

    public int MaxArchiveEntries { get; init; } = 10_000;

    public int MaxChunksPerEntry { get; init; } = 1_024;

    public int MaxIndexEntries { get; init; } = 100_000;

    public bool AllowUntestedVersions
    {
        get; init;
    }

    internal void Validate()
    {
        if (MaxFileBytes < 85 || MaxDecodedBlockBytes < 1 || MaxTotalDecodedBytes < 1 || MaxDecodedBlocks < 1 || MaxStringBytes < 1 || MaxArchiveEntries < 1 || MaxChunksPerEntry < 1 || MaxIndexEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(WindevOpenOptions), "All reader limits must be positive.");
        }
    }
}

public class WindevFormatException : IOException
{
    public int? Offset { get; }

    public WindevFormatException(string message, int? offset = null) : base(offset.HasValue ? $"{message} (offset 0x{offset:X})" : message) => Offset = offset;
}

public sealed class UnsupportedWindevFormatException : WindevFormatException
{
    public UnsupportedWindevFormatException(string message, int? offset = null) : base(message, offset)
    {
    }
}

public sealed class WindevDecodedBlock
{
    public int Offset { get; }

    public int StoredByteLength { get; }

    public string Codec { get; }

    public int DecodedByteLength => Data.Length;

    [JsonIgnore]
    public ReadOnlyMemory<byte> Data { get; }

    public IReadOnlyList<WindevString> Strings { get; }

    internal WindevDecodedBlock(
        int offset,
        int length,
        string codec,
        byte[] data,
        WindevOpenOptions options,
        bool legacyText = false)
    {
        Offset = offset;

        StoredByteLength = length;

        Codec = codec;

        Data = data;

        Strings = legacyText
            ? Internal.BinaryText.FindZeroTerminatedCandidates(data, options.MaxStringBytes)
            : Internal.BinaryText.FindStrings(data, options.MaxStringBytes);
    }
}

public sealed class WindevSection
{
    public int Index { get; }

    public byte RawTag { get; }

    public int Offset { get; }

    public int ByteLength => RawData.Length;

    [JsonIgnore]
    public ReadOnlyMemory<byte> RawData { get; }

    public IReadOnlyList<WindevString> Strings { get; }

    public IReadOnlyList<WindevDecodedBlock> DecodedBlocks { get; }

    internal WindevSection(
        int index,
        byte tag,
        int offset,
        ReadOnlyMemory<byte> data,
        IReadOnlyList<WindevDecodedBlock> decoded,
        WindevOpenOptions options)
    {
        Index = index;

        RawTag = tag;

        Offset = offset;

        RawData = data;

        DecodedBlocks = decoded;

        Strings = Internal.BinaryText.FindStrings(data.Span, options.MaxStringBytes);
    }

    public uint ReadUInt32(int offset) => Internal.Binary.U32(RawData.Span, offset);
}
