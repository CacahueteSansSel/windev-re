using System.Text.RegularExpressions;
using WindevReader.Internal;

namespace WindevReader;

public sealed class WdeWindevReport : WindevDocument
{
    public IReadOnlyList<WindevDecodedBlock> PropertyBuffers { get; }

    public IReadOnlyList<WindevUiElement> Elements { get; }

    public string? NoDataMessage { get; }

    private WdeWindevReport(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Report, container, options)
    {
        PropertyBuffers = Sections.ElementAtOrDefault(1)?.DecodedBlocks ?? Array.Empty<WindevDecodedBlock>();

        if (PropertyBuffers.Count == 0)
        {
            throw new WindevFormatException("Report property section could not be decoded");
        }

        Elements = UiReader.Elements(PropertyBuffers, Name, CodeBlocks);

        NoDataMessage = PropertyBuffers.FirstOrDefault()?.Strings.Skip(1).FirstOrDefault()?.Value;

        Warn(
            "PartialReportSemantics",
            "Report property buffers, named elements and source events are accessible. Pagination, styles and data-source bindings remain binary.",
            1);
    }

    public new static WdeWindevReport Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WdeWindevReport(filename, Load(filename, 3, options), options);
    }
}
