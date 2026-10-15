using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public enum HfsqlPhysicalFileKind { Data = 1, Index = 2, Memo = 3 }
public enum HfsqlPasswordStatus { Unknown }

/// <summary>Verified clear header. Raw flags and mode bytes deliberately have no inferred cryptographic names.</summary>
public sealed record HfsqlPhysicalHeader(uint ContainerRevision, uint RawDocumentType, ushort ProductRevision,
    ushort WindevVersion, ushort RawWordAt16, ushort RawWordAt18, string AnalysisGuid, string TableGuid,
    ushort RawFlagsAt86, byte RawModeAt88, byte RawModeAt89);

/// <summary>Structural access to physical HFSQL files. Supported clear fixed records and flat index pages require explicit APIs.</summary>
public abstract class HfsqlPhysicalFile : IWindevFile
{
    public string FileName { get; }
    public string Name { get; }
    public HfsqlPhysicalFileKind Kind { get; }
    public HfsqlPhysicalHeader Header { get; }
    public HfsqlPasswordStatus PasswordStatus => HfsqlPasswordStatus.Unknown;
    /// <summary>Automatic schema and live-record discovery remain unsupported. Use an explicit fixed layout for clear FIC slots.</summary>
    public bool CanReadRecords => false;
    public IReadOnlyList<WindevDiagnostic> Diagnostics { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> RawData { get; }
    /// <summary>Bytes from offset 90, without assuming PCS-document sections, record boundaries or a cipher.</summary>
    [JsonIgnore] public ReadOnlyMemory<byte> UninterpretedBody => RawData[90..];

    private protected HfsqlPhysicalFile(string filename, byte[] data, HfsqlPhysicalFileKind kind)
    {
        FileName = Path.GetFullPath(filename); Name = Path.GetFileNameWithoutExtension(filename); RawData = data; Kind = kind;
        string analysis = ReadGuid(data, 20), table = ReadGuid(data, 53);
        Header = new(20, Binary.U32(data, 8), Binary.U16(data, 12), Binary.U16(data, 14), Binary.U16(data, 16),
            Binary.U16(data, 18), analysis, table, Binary.U16(data, 86), data[88], data[89]);
        var diagnostics = new List<WindevDiagnostic>
        {
            new("PhysicalHfsqlPayloadNotDecoded", DiagnosticSeverity.Warning,
                "Clear headers and explicitly exposed structural directories are decoded. Automatic record discovery, general B-tree traversal, memo allocation and protection are not decoded. Clear FIC slots can be read with an explicit caller-supplied layout."),
            new("UnknownPasswordStatus", DiagnosticSeverity.Information,
                "No password or encryption algorithm is inferred from entropy or raw mode bytes. A readable WDD does not unlock physical files.")
        };
        if (Header.WindevVersion != 31 && !(kind is HfsqlPhysicalFileKind.Data or HfsqlPhysicalFileKind.Index && Header.WindevVersion == 28)) diagnostics.Add(new("UntestedVersion", DiagnosticSeverity.Warning, "HFSQL FIC/NDX headers validated with WINDEV 28 and 31; MMO with 31."));
        Diagnostics = diagnostics.AsReadOnly();
    }

    private static string ReadGuid(ReadOnlySpan<byte> data, int offset)
    {
        Binary.Require(data, offset, 33);
        if (data[offset + 32] != 0) throw new WindevFormatException("Unterminated HFSQL GUID", offset);
        foreach (byte value in data.Slice(offset, 32))
            if (value is not (>= (byte)'0' and <= (byte)'9') and not (>= (byte)'A' and <= (byte)'F') and not (>= (byte)'a' and <= (byte)'f'))
                throw new WindevFormatException("Invalid HFSQL GUID", offset);
        return Encoding.ASCII.GetString(data.Slice(offset, 32));
    }

    /// <summary>Matches physical companions by both analysis and table GUID, regardless of filename.</summary>
    public bool HasSameIdentityAs(HfsqlPhysicalFile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Header.AnalysisGuid, other.Header.AnalysisGuid, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Header.TableGuid, other.Header.TableGuid, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesAnalysis(WddWindevAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return string.Equals(Header.AnalysisGuid, analysis.Guid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns a description only when both GUIDs match. Matching metadata does not decrypt data.</summary>
    public WindevDataTable? FindTableDefinition(WddWindevAnalysis analysis) => MatchesAnalysis(analysis)
        ? analysis.Tables.FirstOrDefault(t => string.Equals(t.Guid, Header.TableGuid, StringComparison.OrdinalIgnoreCase)) : null;
}

public sealed class FicHfsqlDataFile : HfsqlPhysicalFile
{
    private FicHfsqlDataFile(string file, byte[] data) : base(file, data, HfsqlPhysicalFileKind.Data) { }
    /// <summary>Reads physical slots with a caller-supplied layout; does not infer schema, decrypt, or filter deleted records.</summary>
    public HfsqlFixedRecordView ReadFixedRecords(HfsqlFixedRecordLayout layout) => new(RawData, layout);
    /// <summary>Uses a matching WINDEV 28/31 WDD to name/type fixed fields. Data offset and physical count remain caller-supplied.
    /// Only the observed 9-byte-prefix, fixed-field profile is supported. Does not classify live/deleted records.</summary>
    public HfsqlFixedRecordLayout CreateFixedRecordLayout(WddWindevAnalysis analysis, int dataOffset, int recordCount)
    {
        var table = FindTableDefinition(analysis) ?? throw new WindevFormatException("WDD analysis/table GUIDs do not match this FIC");
        if (Header.WindevVersion is not (28 or 31) || analysis.Header.WindevVersion != Header.WindevVersion)
            throw new UnsupportedWindevFormatException("Schema-assisted fixed layout requires matching WINDEV 28 or 31 profiles");
        var fields = new List<HfsqlFixedField>(); int offset = 9;
        foreach (var field in table.Fields)
        {
            // The observed MOISJOUR compound key has no physical value in the FIC slot.
            if (analysis.Header.WindevVersion == 31 && field.IsCompositeKey) continue;
            var type = (field.RawTypeCode, field.StorageSize) switch
            {
                (24, 8) or (25, 8) => HfsqlFixedFieldType.Int64,
                (2, > 0) when field.StorageSize == field.Size + 1 => HfsqlFixedFieldType.Cp1252Z,
                (5, 4) => HfsqlFixedFieldType.Int32,
                (37, 1) => HfsqlFixedFieldType.Boolean8,
                (36, 1) => HfsqlFixedFieldType.UInt8,
                (14, 9) when field.Size == 8 => HfsqlFixedFieldType.Date8Z,
                _ => throw new UnsupportedWindevFormatException($"Unsupported schema-assisted physical field: {field.Name}")
            };
            fields.Add(new(field.Name, offset, field.StorageSize, type)); offset = checked(offset + field.StorageSize);
        }
        if (table.RawRecordByteLength != offset)
            throw new UnsupportedWindevFormatException("WDD slot length does not match the observed fixed-field profile");
        var layout = new HfsqlFixedRecordLayout(dataOffset, offset, recordCount, fields.AsReadOnly());
        _ = ReadFixedRecords(layout); // Validate physical boundaries before returning a usable layout.
        return layout;
    }
    public static FicHfsqlDataFile Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, FileInput.Read(filename, 0x70001, options, 90)); }
}

/// <summary>Fields at relative offsets 0, 6, 10, 14, 16, 24, 28, 36, 44..59 in a 60-byte descriptor.
/// No checksum, key type, tree root or free-page semantics are assigned without further evidence.</summary>
public sealed record HfsqlIndexDescriptor(int Offset, ushort RawWordAt0, uint RawWordAt6,
    uint RawWordAt10, ushort RawWordAt14, ulong RawPointerAt16, uint RawLengthAt24,
    ulong RawPointerAt28, ulong RawPointerAt36, IReadOnlyList<uint> RawTailWords);

public sealed class NdxHfsqlIndexFile : HfsqlPhysicalFile
{
    public ushort DeclaredIndexCount { get; }
    public int DirectoryOffset { get; }
    /// <summary>Positions read from the terminal u64 directory. Index key/B-tree semantics remain unsupported.</summary>
    public IReadOnlyList<int> IndexDescriptorOffsets { get; }
    /// <summary>60-byte descriptors referenced by the terminal directory. Pointers remain raw structural observations.</summary>
    public IReadOnlyList<HfsqlIndexDescriptor> IndexDescriptors { get; }
    private readonly WindevOpenOptions _options;
    private NdxHfsqlIndexFile(string file, byte[] data, WindevOpenOptions options) : base(file, data, HfsqlPhysicalFileKind.Index)
    {
        _options = options;
        DeclaredIndexCount = Binary.U16(data, 107);
        DirectoryOffset = Binary.Offset(Binary.U64(data, 111), data.Length);
        if (DirectoryOffset < 119 || (long)DirectoryOffset + (long)DeclaredIndexCount * 8 != data.Length)
            throw new WindevFormatException("Unsupported or truncated NDX terminal directory", 111);
        var positions = new List<int>(); int previous = 118;
        for (int i = 0; i < DeclaredIndexCount; i++)
        {
            int position = Binary.Offset(Binary.U64(data, DirectoryOffset + i * 8), data.Length);
            if (position <= previous || position >= DirectoryOffset)
                throw new WindevFormatException("NDX descriptor positions are not ordered inside the body", DirectoryOffset + i * 8);
            positions.Add(position); previous = position;
        }
        IndexDescriptorOffsets = positions.AsReadOnly();
        var descriptors = new List<HfsqlIndexDescriptor>();
        foreach (int position in positions)
        {
            Binary.Require(data, position, 60);
            if (position > DirectoryOffset - 60 || Binary.U32(data, position + 2) != 60)
                throw new UnsupportedWindevFormatException("Unsupported NDX descriptor size", position + 2);
            descriptors.Add(new(position, Binary.U16(data, position), Binary.U32(data, position + 6),
                Binary.U32(data, position + 10), Binary.U16(data, position + 14), Binary.U64(data, position + 16),
                Binary.U32(data, position + 24), Binary.U64(data, position + 28), Binary.U64(data, position + 36),
                Array.AsReadOnly(Enumerable.Range(0, 4).Select(i => Binary.U32(data, position + 44 + 4 * i)).ToArray())));
        }
        IndexDescriptors = descriptors.AsReadOnly();
    }
    public static NdxHfsqlIndexFile Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, FileInput.Read(filename, 0x70002, options, 119), options); }

    /// <summary>Reads the observed clear WINDEV 31 flat page (tag 7, 29-byte header). No B-tree traversal or key-name inference.</summary>
    public HfsqlFlatIndex ReadFlatIndex(int index)
    {
        if ((uint)index >= (uint)IndexDescriptors.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (Header.WindevVersion != 31 || Header.RawModeAt88 != 0)
            throw new UnsupportedWindevFormatException("Clear flat index profile was validated with WINDEV 31 mode 0 only");
        var descriptor = IndexDescriptors[index];
        return HfsqlFlatIndex.Read(RawData, descriptor, DirectoryOffset, _options.MaxIndexEntries);
    }

    /// <summary>Bytes between an announced descriptor and the next one (or the directory), without B-tree interpretation.</summary>
    public ReadOnlyMemory<byte> ReadIndexRegion(int index)
    {
        if (index < 0 || index >= IndexDescriptorOffsets.Count) throw new ArgumentOutOfRangeException(nameof(index));
        int start = IndexDescriptorOffsets[index], end = index + 1 < IndexDescriptorOffsets.Count ? IndexDescriptorOffsets[index + 1] : DirectoryOffset;
        return RawData.Slice(start, end - start);
    }
}

public sealed class MmoHfsqlMemoFile : HfsqlPhysicalFile
{
    /// <summary>Fourteen raw u64 slots starting at offset 98 in the observed header profile; semantics are not mapped.</summary>
    public IReadOnlyList<ulong> RawHeaderSlots { get; }
    public ushort RawWordAt90 { get; }
    public ushort RawWordAt92 { get; }
    public uint RawWordAt94 { get; }
    public bool HasBytesAfterHeader => RawData.Length > 210;
    private MmoHfsqlMemoFile(string file, byte[] data) : base(file, data, HfsqlPhysicalFileKind.Memo)
    {
        RawWordAt90 = Binary.U16(data, 90); RawWordAt92 = Binary.U16(data, 92); RawWordAt94 = Binary.U32(data, 94);
        RawHeaderSlots = Array.AsReadOnly(Enumerable.Range(0, 14).Select(i => Binary.U64(data, 98 + i * 8)).ToArray());
    }
    public static MmoHfsqlMemoFile Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, FileInput.Read(filename, 0x70003, options, 210)); }
}
