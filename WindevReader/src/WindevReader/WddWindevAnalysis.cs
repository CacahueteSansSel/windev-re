using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class WddWindevAnalysis : WindevDocument
{
    public string Guid { get; }

    public uint Generation { get; }

    public IReadOnlyList<WindevDataTable> Tables { get; }

    public IReadOnlyList<WindevSchemaDirectoryEntry> SchemaDirectory { get; }

    public uint DeclaredRelationCount { get; }

    public IReadOnlyList<WindevDataRelation> Relations { get; }

    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedSchema { get; }

    private WddWindevAnalysis(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.Analysis, container, options)
    {
        WindevDecodedBlock block = Sections.ElementAtOrDefault(1)?.DecodedBlocks.FirstOrDefault() ?? throw new WindevFormatException("Missing WDD schema section");
        DecodedSchema schema = SchemaReader.Read(block.Data, options, container.Header.WindevVersion);

        Name = Path.GetFileNameWithoutExtension(schema.Name);

        Guid = schema.Guid;

        Generation = schema.Generation;

        Tables = schema.Tables;

        SchemaDirectory = schema.Directory;

        DecodedSchema = schema.Data;

        DeclaredRelationCount = schema.RelationCount;

        Relations = schema.Relations;

        if (DeclaredRelationCount != Relations.Count)
        {
            Warn(
                "UndecodedRelations",
                "Relationship records are present but not semantically decoded; see SchemaDirectory and DecodedSchema.");
        }

        if (Relations.Count != 0)
        {
            Warn(
                "PartialRelationProperties",
                "Relation endpoints are decoded; cardinalities and constraint flags remain raw.");
        }

        Warn(
            "PartialSchemaProperties",
            "Table and field definitions are decoded. Key rules, defaults, connections and remaining flags retain their raw representation.");
    }

    public new static WddWindevAnalysis Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WddWindevAnalysis(filename, Load(filename, 0x00070004, options), options);
    }
}
