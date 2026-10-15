using System.Text.RegularExpressions;
using WindevReader.Internal;

namespace WindevReader;
internal static class UiReader
{
    internal static readonly Regex Identifier = new(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.CultureInvariant);
    private static readonly string[] Prefixes = ["BTN_", "BT_", "SAI_", "EDT_", "LIB_", "TABLE_", "COL_", "COMBO_", "CMB_", "INT_", "SEL_", "IMG_", "ZR_", "GR_", "OPT_", "MENU_", "ONG_", "TAB_", "FEN_", "DEBUT_", "HAUT_", "BAS_", "FIN_", "CHAMP"];
    public static IReadOnlyList<WindevUiElement> Elements(IReadOnlyList<WindevDecodedBlock> buffers, string name, IReadOnlyList<WindevCodeBlock> code)
    {
        var owners = code.Select(c => c.OwnerName).ToHashSet(StringComparer.Ordinal);
        var result = new List<WindevUiElement>();
        foreach (var buffer in buffers)
        {
            var strings = buffer.Strings;
            for (int i = 0; i < strings.Count; i++)
            {
                var s = strings[i]; string value = s.Value;
                if (value == name || !Identifier.IsMatch(value)) continue;
                bool candidate = Prefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                    owners.Contains(value) || value is "_Menu" or "CORPS" or "NUMNBPAGES" or "TITREDOC";
                if (!candidate) continue;
                string kind = value.StartsWith("OPT_") ? "MenuOption" : value.StartsWith("COL_") ? "Column" :
                    value.StartsWith("BTN_") || value.StartsWith("BT_") ? "Button" : value.StartsWith("TABLE_") ? "Table" :
                    value.StartsWith("SAI_") ? "Input" : value.StartsWith("LIB_") ? "Label" : value.StartsWith("IMG_") ? "Image" :
                    value.StartsWith("GR_") ? "Group" : value.StartsWith("MENU_") || value == "_Menu" ? "Menu" :
                    value is "CORPS" || value.StartsWith("DEBUT_") || value.StartsWith("HAUT_") || value.StartsWith("BAS_") || value.StartsWith("FIN_") ? "ReportBlock" : "Unknown";
                result.Add(new(value, kind, buffer.Offset, s.Offset, strings.Skip(i + 1).Take(3).ToArray()));
            }
        }
        return result.DistinctBy(e => e.Name).ToArray();
    }
}
public sealed class WdwWindevWindow : WindevDocument
{
    public IReadOnlyList<WindevDecodedBlock> PropertyBuffers { get; }
    public IReadOnlyList<WindevUiElement> Elements { get; }
    public string? Caption { get; }
    public WindevBounds? Bounds { get; }
    /// <summary>Complete BMP recovered from the LZW envelope of section 4, when recognized.</summary>
    public WindevWindowPreview? Preview { get; }
    private WdwWindevWindow(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Window, container, options)
    {
        PropertyBuffers = Sections.ElementAtOrDefault(1)?.DecodedBlocks ?? Array.Empty<WindevDecodedBlock>();
        if (PropertyBuffers.Count == 0) throw new WindevFormatException("Window property section could not be decoded");
        Elements = UiReader.Elements(PropertyBuffers, Name, CodeBlocks);
        var imageSection = Sections.ElementAtOrDefault(4);
        var image = imageSection?.RawTag == 8 ? imageSection.DecodedBlocks.FirstOrDefault(b => b.Offset == 0) : null;
        if (image is not null && image.Data.Span.StartsWith("BM"u8))
        {
            try { Preview = WindevWindowPreview.Parse(image.Data); }
            catch (UnsupportedWindevFormatException error) { Warn("UnsupportedPreviewBitmap", error.Message, 4); }
        }
        var root = PropertyBuffers.FirstOrDefault();
        if (root is not null)
        {
            var first = root.Strings.FirstOrDefault();
            if (first?.Value == Name)
            {
                int p = first.Offset + first.ByteLength + 8;
                if (p <= root.Data.Length - 24 && Binary.U32(root.Data.Span, 4) == 610)
                {
                    Bounds = new((int)Binary.U32(root.Data.Span, p), (int)Binary.U32(root.Data.Span, p + 4),
                        (int)Binary.U32(root.Data.Span, p + 8), (int)Binary.U32(root.Data.Span, p + 12));
                    int captionOffset = first.Offset + first.ByteLength + 571;
                    if (BinaryText.TryString(root.Data.Span, captionOffset, options.MaxStringBytes, out var caption)) Caption = caption.Value;
                    else if (captionOffset < root.Data.Length && root.Data.Span[captionOffset] == 0) Caption = "";
                }
            }
        }
        Warn("PartialUiSemantics", "Property buffers and source events are decoded. Element kinds are inferred from names; layout/styles/bindings and hierarchy are not fully mapped.", 1);
    }
    public new static WdwWindevWindow Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 2, options), options); }
}
public sealed class WdeWindevReport : WindevDocument
{
    public IReadOnlyList<WindevDecodedBlock> PropertyBuffers { get; }
    public IReadOnlyList<WindevUiElement> Elements { get; }
    public string? NoDataMessage { get; }
    private WdeWindevReport(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Report, container, options)
    {
        PropertyBuffers = Sections.ElementAtOrDefault(1)?.DecodedBlocks ?? Array.Empty<WindevDecodedBlock>();
        if (PropertyBuffers.Count == 0) throw new WindevFormatException("Report property section could not be decoded");
        Elements = UiReader.Elements(PropertyBuffers, Name, CodeBlocks);
        NoDataMessage = PropertyBuffers.FirstOrDefault()?.Strings.Skip(1).FirstOrDefault()?.Value;
        Warn("PartialReportSemantics", "Report property buffers, named elements and source events are accessible. Pagination, styles and data-source bindings remain binary.", 1);
    }
    public new static WdeWindevReport Open(string filename, WindevOpenOptions? options = null)
    { options ??= new(); return new(filename, Load(filename, 3, options), options); }
}
