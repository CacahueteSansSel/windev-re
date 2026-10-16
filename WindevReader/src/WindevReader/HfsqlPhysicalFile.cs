using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public enum HfsqlPhysicalFileKind
{
    Data = 1,
    Index = 2,
    Memo = 3
}

public enum HfsqlPasswordStatus
{
    Unknown
}

/// <summary>Verified clear header. Raw flags and mode bytes deliberately have no inferred cryptographic names.</summary>
public sealed record HfsqlPhysicalHeader(
    uint ContainerRevision,
    uint RawDocumentType,
    ushort ProductRevision,
    ushort WindevVersion,
    ushort RawWordAt16,
    ushort RawWordAt18,
    string AnalysisGuid,
    string TableGuid,
    ushort RawFlagsAt86,
    byte RawModeAt88,
    byte RawModeAt89);

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

    [JsonIgnore]
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Bytes from offset 90, without assuming PCS-document sections, record boundaries or a cipher.</summary>
    [JsonIgnore]
    public ReadOnlyMemory<byte> UninterpretedBody => RawData[90..];

    private protected HfsqlPhysicalFile(string filename, byte[] data, HfsqlPhysicalFileKind kind)
    {
        FileName = Path.GetFullPath(filename);

        Name = Path.GetFileNameWithoutExtension(filename);

        RawData = data;

        Kind = kind;

        string analysis = ReadGuid(data, 20);
        string table = ReadGuid(data, 53);

        Header = new HfsqlPhysicalHeader(
            20,
            Binary.U32(data, 8),
            Binary.U16(data, 12),
            Binary.U16(data, 14),
            Binary.U16(data, 16),
            Binary.U16(data, 18),
            analysis,
            table,
            Binary.U16(data, 86),
            data[88],
            data[89]);

        List<WindevDiagnostic> diagnostics = new List<WindevDiagnostic>
        {
            new(
                "PhysicalHfsqlPayloadNotDecoded",
                DiagnosticSeverity.Warning,
                "Clear headers and explicitly exposed structural directories are decoded. Automatic record discovery, general B-tree traversal, memo allocation and protection are not decoded. Clear FIC slots can be read with an explicit caller-supplied layout."),
            new(
                "UnknownPasswordStatus",
                DiagnosticSeverity.Information,
                "No password or encryption algorithm is inferred from entropy or raw mode bytes. A readable WDD does not unlock physical files.")
        };

        if (Header.WindevVersion is not (15 or 24 or 25 or 29 or 31) && !(kind is HfsqlPhysicalFileKind.Data or HfsqlPhysicalFileKind.Index && Header.WindevVersion == 28))
        {
            diagnostics.Add(new WindevDiagnostic(
                "UntestedVersion",
                DiagnosticSeverity.Warning,
                "HFSQL FIC/NDX headers validated with WINDEV 15, 24, 25, 28, 29 and 31; MMO with 15, 24, 25, 29 and 31."));
        }

        Diagnostics = diagnostics.AsReadOnly();
    }

    private static string ReadGuid(ReadOnlySpan<byte> data, int offset)
    {
        Binary.Require(data, offset, 33);

        if (data[offset + 32] != 0)
        {
            throw new WindevFormatException("Unterminated HFSQL GUID", offset);
        }

        foreach (byte value in data.Slice(offset, 32))
        {
            if (value is not (>= (byte)'0' and <= (byte)'9') and not (>= (byte)'A' and <= (byte)'F') and not (>= (byte)'a' and <= (byte)'f'))
            {
                throw new WindevFormatException("Invalid HFSQL GUID", offset);
            }
        }

        return Encoding.ASCII.GetString(data.Slice(offset, 32));
    }

    /// <summary>Matches physical companions by both analysis and table GUID, regardless of filename.</summary>
    public bool HasSameIdentityAs(HfsqlPhysicalFile other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Header.AnalysisGuid, other.Header.AnalysisGuid, StringComparison.OrdinalIgnoreCase) && string.Equals(Header.TableGuid, other.Header.TableGuid, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesAnalysis(WddWindevAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return string.Equals(Header.AnalysisGuid, analysis.Guid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns a description only when both GUIDs match. Matching metadata does not decrypt data.</summary>
    public WindevDataTable? FindTableDefinition(WddWindevAnalysis analysis) => MatchesAnalysis(analysis) ? analysis.Tables.FirstOrDefault(table => string.Equals(table.Guid, Header.TableGuid, StringComparison.OrdinalIgnoreCase)) : null;
}

/// <summary>Fields at relative offsets 0, 6, 10, 14, 16, 24, 28, 36, 44..59 in a 60-byte descriptor.
/// No checksum, key type, tree root or free-page semantics are assigned without further evidence.</summary>
public sealed record HfsqlIndexDescriptor(
    int Offset,
    ushort RawWordAt0,
    uint RawWordAt6,
    uint RawWordAt10,
    ushort RawWordAt14,
    ulong RawPointerAt16,
    uint RawLengthAt24,
    ulong RawPointerAt28,
    ulong RawPointerAt36,
    IReadOnlyList<uint> RawTailWords);
