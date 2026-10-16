using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class WdpWindevProject : WindevDocument
{
    public IReadOnlyList<WindevProjectReference> References { get; }

    public IReadOnlyList<string> ConfigurationNames { get; }

    private WdpWindevProject(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Project, container, options)
    {
        WindevSection? configurationSection = Sections.FirstOrDefault(section => section.Index == 13);
        WindevDecodedBlock? configurationBlock = configurationSection?.DecodedBlocks.FirstOrDefault(decodedBlock => decodedBlock.Offset == 12);
        IReadOnlyList<WindevString> strings = configurationBlock?.Strings ?? configurationSection?.Strings ?? Array.Empty<WindevString>();
        ReadOnlyMemory<byte> configurationData = configurationBlock?.Data ?? configurationSection?.RawData ?? ReadOnlyMemory<byte>.Empty;
        List<WindevProjectReference> references = new List<WindevProjectReference>();
        string configuration = configurationBlock is null ? strings.FirstOrDefault()?.Value ?? "" : "";

        ConfigurationNames = configuration == "" ? Array.Empty<string>() : new string[]
        {
            configuration
        };

        if (configurationSection is not null)
        {
            // Configuration elements are framed strings in size-delimited records. The first has an element count.
            foreach (WindevString value in strings)
            {
                int cursor = value.Offset;

                if (cursor < 4 || Binary.U32(configurationData.Span, cursor - 4) != value.ByteLength + 20)
                {
                    continue;
                }

                references.Add(new WindevProjectReference(value.Value, cursor, configuration));
            }
        }

        References = references.AsReadOnly();

        if (Sections.Count > 1 && Sections[1].ByteLength != 0)
        {
            Warn(
                "OpaqueProjectSettings",
                "Project settings in section 1 are preserved as bytes; their encoding is not decoded yet.",
                1);
        }

        if (References.Count == 0)
        {
            Warn(
                "NoConfigurationDirectory",
                "No supported build configuration directory was found (possibly a compilation cache).");
        }
    }

    public new static WdpWindevProject Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WdpWindevProject(filename, Load(filename, 0x1001, options), options);
    }
}
