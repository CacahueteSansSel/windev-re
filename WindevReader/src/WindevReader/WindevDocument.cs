using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public abstract class WindevDocument : IWindevFile
{
    public string FileName { get; }

    public WindevDocumentKind Kind { get; }

    public string Name
    {
        get; protected set;
    }

    public WindevHeader Header { get; }

    public IReadOnlyList<WindevSection> Sections { get; }

    public IReadOnlyList<WindevCodeBlock> CodeBlocks { get; }

    public IReadOnlyList<WindevDiagnostic> Diagnostics => diagnostics.AsReadOnly();

    private readonly List<WindevDiagnostic> diagnostics;

    [JsonIgnore]
    public ReadOnlyMemory<byte> RawData { get; }

    private protected WindevDocument(string filename, WindevDocumentKind kind, PcsContainer container, WindevOpenOptions options)
    {
        FileName = Path.GetFullPath(filename);

        Kind = kind;

        Header = container.Header;

        Sections = container.Sections;

        RawData = container.RawData;

        diagnostics = container.Diagnostics;

        Name = ReadName(filename, options);

        WindevDecodedBlock? code = Sections.FirstOrDefault(section => section.Index == 2 && section.RawTag == 2)?.DecodedBlocks.FirstOrDefault(decodedBlock => decodedBlock.Offset == 4);

        CodeBlocks = code is null ? Array.Empty<WindevCodeBlock>() : CodeReader.Read(code.Data.Span, Name, options, Header.WindevVersion);
    }

    private string ReadName(string filename, WindevOpenOptions options)
    {
        WindevSection? identitySection = Sections.FirstOrDefault();

        if (Header.WindevVersion == 15 && Header.RawDocumentType is 0x1001 or 2 && identitySection is not null && identitySection.ByteLength > 4)
        {
            int nameOffset = 4;

            return BinaryText.CString(identitySection.RawData.Span, ref nameOffset, options.MaxStringBytes, true);
        }

        return identitySection?.Strings.FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(filename);
    }

    private protected void Warn(string code, string message, int? section = null) => diagnostics.Add(new WindevDiagnostic(code, DiagnosticSeverity.Warning, message, section));

    private protected static PcsContainer Load(string filename, uint expected, WindevOpenOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        PcsContainer result = PcsContainer.Read(filename, options);

        if (result.Header.RawDocumentType != expected)
        {
            throw new UnsupportedWindevFormatException($"Wrong PCS document type: expected 0x{expected:X}, found 0x{result.Header.RawDocumentType:X}", 8);
        }

        return result;
    }

    public static WindevDocument Open(string filename, WindevOpenOptions? options = null) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".wdp" => WdpWindevProject.Open(filename, options),
        ".wdw" => WdwWindevWindow.Open(filename, options),
        ".wdg" => WdgWindevGlobalProcedures.Open(filename, options),
        ".wdd" => WddWindevAnalysis.Open(filename, options),
        ".wde" => WdeWindevReport.Open(filename, options),
        ".wda" => WdaWindevAnalysisSource.Open(filename, options),
        _ => throw new UnsupportedWindevFormatException("Supported extensions: .wdp, .wdw, .wdg, .wdd, .wde, .wda")
    };
}
