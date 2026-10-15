using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public abstract class WindevDocument : IWindevFile
{
    public string FileName { get; }
    public WindevDocumentKind Kind { get; }
    public string Name { get; protected set; }
    public WindevHeader Header { get; }
    public IReadOnlyList<WindevSection> Sections { get; }
    public IReadOnlyList<WindevCodeBlock> CodeBlocks { get; }
    public IReadOnlyList<WindevDiagnostic> Diagnostics => _diagnostics.AsReadOnly();
    private readonly List<WindevDiagnostic> _diagnostics;
    [JsonIgnore] public ReadOnlyMemory<byte> RawData { get; }
    private protected WindevDocument(string filename, WindevDocumentKind kind, PcsContainer container, WindevOpenOptions options)
    {
        FileName = Path.GetFullPath(filename); Kind = kind; Header = container.Header;
        Sections = container.Sections; RawData = container.RawData; _diagnostics = container.Diagnostics;
        Name = Sections.FirstOrDefault()?.Strings.FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(filename);
        var code = Sections.FirstOrDefault(s => s.Index == 2 && s.RawTag == 2)?.DecodedBlocks.FirstOrDefault(b => b.Offset == 4);
        CodeBlocks = code is null ? Array.Empty<WindevCodeBlock>() : CodeReader.Read(code.Data.Span, Name, options);
    }
    private protected void Warn(string code, string message, int? section = null) =>
        _diagnostics.Add(new(code, DiagnosticSeverity.Warning, message, section));
    private protected static PcsContainer Load(string filename, uint expected, WindevOpenOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        var result = PcsContainer.Read(filename, options);
        if (result.Header.RawDocumentType != expected)
            throw new UnsupportedWindevFormatException($"Wrong PCS document type: expected 0x{expected:X}, found 0x{result.Header.RawDocumentType:X}", 8);
        return result;
    }
    public static WindevDocument Open(string filename, WindevOpenOptions? options = null) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".wdp" => WdpWindevProject.Open(filename, options), ".wdw" => WdwWindevWindow.Open(filename, options),
        ".wdg" => WdgWindevGlobalProcedures.Open(filename, options), ".wdd" => WddWindevAnalysis.Open(filename, options),
        ".wde" => WdeWindevReport.Open(filename, options), ".wda" => WdaWindevAnalysisSource.Open(filename, options),
        _ => throw new UnsupportedWindevFormatException("Supported extensions: .wdp, .wdw, .wdg, .wdd, .wde, .wda")
    };
}

public sealed class WdpWindevProject : WindevDocument
{
    public IReadOnlyList<WindevProjectReference> References { get; }
    public IReadOnlyList<string> ConfigurationNames { get; }
    private WdpWindevProject(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Project, container, options)
    {
        var config = Sections.FirstOrDefault(s => s.Index == 13);
        var decoded = config?.DecodedBlocks.FirstOrDefault(b => b.Offset == 12);
        var strings = decoded?.Strings ?? config?.Strings ?? Array.Empty<WindevString>();
        ReadOnlyMemory<byte> configurationData = decoded?.Data ?? config?.RawData ?? ReadOnlyMemory<byte>.Empty;
        var refs = new List<WindevProjectReference>();
        string configuration = decoded is null ? strings.FirstOrDefault()?.Value ?? "" : "";
        ConfigurationNames = configuration == "" ? Array.Empty<string>() : new[] { configuration };
        if (config is not null)
        {
            // Configuration elements are framed strings in size-delimited records. The first has an element count.
            foreach (var value in strings)
            {
                int p = value.Offset;
                if (p < 4 || Binary.U32(configurationData.Span, p - 4) != value.ByteLength + 20) continue;
                refs.Add(new(value.Value, p, configuration));
            }
        }
        References = refs.AsReadOnly();
        if (Sections.Count > 1 && Sections[1].ByteLength != 0)
            Warn("OpaqueProjectSettings", "Project settings in section 1 are preserved as bytes; their encoding is not decoded yet.", 1);
        if (References.Count == 0) Warn("NoConfigurationDirectory", "No supported build configuration directory was found (possibly a compilation cache).");
    }
    public new static WdpWindevProject Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 0x1001, options), options); }
}

public sealed class WdgWindevGlobalProcedures : WindevDocument
{
    public IReadOnlyList<WindevProcedure> Procedures { get; }
    public IReadOnlyList<WindevCodeBlock> GlobalDeclarations { get; }
    private WdgWindevGlobalProcedures(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.GlobalProcedures, container, options)
    {
        GlobalDeclarations = CodeBlocks.Where(b => b.OwnerName == Name).ToArray();
        Procedures = CodeBlocks.Where(b => b.OwnerName != Name).GroupBy(b => b.OwnerName)
            .Select(g => new WindevProcedure(g.Key, g.ToArray())).ToArray();
        Warn("PartialCodeMetadata", "Source lines and numeric event identifiers are decoded; compiled code and editor metadata remain binary.", 2);
    }
    public new static WdgWindevGlobalProcedures Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 7, options), options); }
}

public sealed class WddWindevAnalysis : WindevDocument
{
    public string Guid { get; }
    public uint Generation { get; }
    public IReadOnlyList<WindevDataTable> Tables { get; }
    public IReadOnlyList<WindevSchemaDirectoryEntry> SchemaDirectory { get; }
    public uint DeclaredRelationCount { get; }
    public IReadOnlyList<WindevDataRelation> Relations { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> DecodedSchema { get; }
    private WddWindevAnalysis(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Analysis, container, options)
    {
        var block = Sections.ElementAtOrDefault(1)?.DecodedBlocks.FirstOrDefault() ?? throw new WindevFormatException("Missing WDD schema section");
        var schema = SchemaReader.Read(block.Data, options, container.Header.WindevVersion); Name = Path.GetFileNameWithoutExtension(schema.Name);
        Guid = schema.Guid; Generation = schema.Generation; Tables = schema.Tables; SchemaDirectory = schema.Directory;
        DecodedSchema = schema.Data; DeclaredRelationCount = schema.RelationCount; Relations = schema.Relations;
        if (DeclaredRelationCount != Relations.Count) Warn("UndecodedRelations", "Relationship records are present but not semantically decoded; see SchemaDirectory and DecodedSchema.");
        if (Relations.Count != 0) Warn("PartialRelationProperties", "Relation endpoints are decoded; cardinalities and constraint flags remain raw.");
        Warn("PartialSchemaProperties", "Table and field definitions are decoded. Key rules, defaults, connections and remaining flags retain their raw representation.");
    }
    public new static WddWindevAnalysis Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 0x00070004, options), options); }
}

/// <summary>Additional low-level access to the editable .wda source of an analysis.</summary>
public sealed class WdaWindevAnalysisSource : WindevDocument
{
    private WdaWindevAnalysisSource(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.AnalysisSource, container, options) =>
        Warn("LowLevelAnalysisSource", ".wda exposes decompressed blocks and strings. Use WddWindevAnalysis for typed tables and fields.");
    public new static WdaWindevAnalysisSource Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 0x00080002, options), options); }
}
