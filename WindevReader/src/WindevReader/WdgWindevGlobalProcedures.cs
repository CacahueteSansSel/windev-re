using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class WdgWindevGlobalProcedures : WindevDocument
{
    public IReadOnlyList<WindevProcedure> Procedures { get; }

    public IReadOnlyList<WindevCodeBlock> GlobalDeclarations { get; }

    private WdgWindevGlobalProcedures(string file, PcsContainer container, WindevOpenOptions options) : base(file, WindevDocumentKind.GlobalProcedures, container, options)
    {
        GlobalDeclarations = CodeBlocks.Where(codeBlock => codeBlock.OwnerName == Name).ToArray();

        Procedures = CodeBlocks.Where(codeBlock => codeBlock.OwnerName != Name).GroupBy(codeBlock => codeBlock.OwnerName).Select(group => new WindevProcedure(group.Key, group.ToArray())).ToArray();

        Warn(
            "PartialCodeMetadata",
            "Source lines and numeric event identifiers are decoded; compiled code and editor metadata remain binary.",
            2);
    }

    public new static WdgWindevGlobalProcedures Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new WdgWindevGlobalProcedures(filename, Load(filename, 7, options), options);
    }
}
