using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

/// <summary>Additional low-level access to the editable .wda source of an analysis.</summary>
public sealed class WdaWindevAnalysisSource : WindevDocument
{
    private WdaWindevAnalysisSource(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.AnalysisSource, container, options) => Warn(
        "LowLevelAnalysisSource",
        ".wda exposes decompressed blocks and strings. Use WddWindevAnalysis for typed tables and fields.");

    public new static WdaWindevAnalysisSource Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WdaWindevAnalysisSource(filename, Load(filename, 0x00080002, options), options);
    }
}
