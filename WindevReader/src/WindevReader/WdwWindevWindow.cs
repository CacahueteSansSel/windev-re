using System.Text.RegularExpressions;
using WindevReader.Internal;

namespace WindevReader;

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

        if (PropertyBuffers.Count == 0)
        {
            throw new WindevFormatException("Window property section could not be decoded");
        }

        Elements = UiReader.Elements(PropertyBuffers, Name, CodeBlocks);

        WindevSection? imageSection = Sections.ElementAtOrDefault(4);
        WindevDecodedBlock? image = imageSection?.RawTag == 8 ? imageSection.DecodedBlocks.FirstOrDefault(decodedBlock => decodedBlock.Offset == 0) : null;

        if (image is not null && image.Data.Span.StartsWith("BM"u8))
        {
            try
            {
                Preview = WindevWindowPreview.Parse(image.Data);
            }
            catch (UnsupportedWindevFormatException error)
            {
                Warn("UnsupportedPreviewBitmap", error.Message, 4);
            }
        }

        WindevDecodedBlock? root = PropertyBuffers.FirstOrDefault();

        if (root is not null && Header.WindevVersion == 15)
        {
            (Bounds, Caption) = ReadLegacyRoot(root.Data.Span, Name, options.MaxStringBytes);

            Warn(
                "HeuristicLegacyStrings",
                "Legacy property strings are zero-terminated CP1252 candidates. Root geometry and caption use checked offsets; remaining candidates may include binary noise.",
                1);
        }
        else if (root is not null)
        {
            WindevString? first = root.Strings.FirstOrDefault();

            if (first?.Value == Name)
            {
                int cursor = first.Offset + first.ByteLength + 8;
                int descriptorOffset = Header.WindevVersion is 24 or 25 ? 0 : 4;
                uint descriptor = Header.WindevVersion is 24 or 25 or 29 ? 607u : 610u;

                if (first.Offset == descriptorOffset + 4 && cursor <= root.Data.Length - 24 && Binary.U32(root.Data.Span, descriptorOffset) == descriptor)
                {
                    Bounds = new WindevBounds(
                        (int)Binary.U32(root.Data.Span, cursor),
                        (int)Binary.U32(root.Data.Span, cursor + 4),
                        (int)Binary.U32(root.Data.Span, cursor + 8),
                        (int)Binary.U32(root.Data.Span, cursor + 12));

                    int captionOffset = first.Offset + first.ByteLength + (Header.WindevVersion switch
                    {
                        24 or 25 => 506,
                        29 => 563,
                        _ => 571
                    });

                    if (BinaryText.TryString(root.Data.Span, captionOffset, options.MaxStringBytes, out WindevString caption))
                    {
                        Caption = caption.Value;
                    }
                    else if (captionOffset < root.Data.Length && root.Data.Span[captionOffset] == 0)
                    {
                        Caption = "";
                    }
                }
            }
        }

        Warn(
            "PartialUiSemantics",
            "Property buffers and source events are decoded. Element kinds are inferred from names; layout/styles/bindings and hierarchy are not fully mapped.",
            1);
    }

    private static (WindevBounds? Bounds, string? Caption) ReadLegacyRoot(
        ReadOnlySpan<byte> data,
        string expectedName,
        int maxStringBytes)
    {
        Binary.Require(data, 0, 4);

        if (Binary.U32(data, 0) != 607)
        {
            return (null, null);
        }

        int cursor = 4;
        string name = BinaryText.CString(data, ref cursor, maxStringBytes, true);

        if (name != expectedName)
        {
            return (null, null);
        }

        int boundsOffset = cursor + 8;
        int captionOffset = cursor + 469;

        Binary.Require(data, boundsOffset, 16);

        WindevBounds bounds = new WindevBounds(
            (int)Binary.U32(data, boundsOffset),
            (int)Binary.U32(data, boundsOffset + 4),
            (int)Binary.U32(data, boundsOffset + 8),
            (int)Binary.U32(data, boundsOffset + 12));
        string caption = BinaryText.CString(data, ref captionOffset, maxStringBytes, true);

        return (bounds, caption);
    }

    public new static WdwWindevWindow Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WdwWindevWindow(filename, Load(filename, 2, options), options);
    }
}
