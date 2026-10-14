using System.Text.Json;
using System.Text.Json.Serialization;
using WindevReader;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
try
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: WindevReader.Cli inspect FILE | json FILE [OUTPUT] | code FILE [OUTPUT] | corpus DIRECTORY [OUTPUT]");
        return 2;
    }
    if (args[0] == "corpus")
    {
        var reports = new List<object>(); bool failed = false;
        var supported = new HashSet<string>([".wdp", ".wdw", ".wdg", ".wdd", ".wda", ".wde"], StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Where(f => supported.Contains(Path.GetExtension(f))).Order())
        {
            try { var doc = WindevDocument.Open(file); reports.Add(new { File = Path.GetRelativePath(args[1], file), Success = true, Summary = Summary(doc) }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { failed = true; reports.Add(new { File = Path.GetRelativePath(args[1], file), Success = false, Error = error.Message }); }
        }
        Write(JsonSerializer.Serialize(reports, jsonOptions), args.ElementAtOrDefault(2)); return failed ? 1 : 0;
    }
    var document = WindevDocument.Open(args[1]);
    string result = args[0] switch
    {
        "inspect" => JsonSerializer.Serialize(Summary(document), jsonOptions),
        "json" => JsonSerializer.Serialize(document, document.GetType(), jsonOptions),
        "code" => string.Join("\n\n", document.CodeBlocks.Select(b => $"// {b.OwnerName} — événement {b.EventId}, langue {b.LanguageId}\n{b.Source}")),
        _ => throw new ArgumentException("Unknown command")
    };
    Write(result, args.ElementAtOrDefault(2)); return 0;
}
catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
{ Console.Error.WriteLine(error.Message); return 1; }

static void Write(string text, string? path)
{
    if (path is null) Console.WriteLine(text); else File.WriteAllText(path, text);
}
static object Summary(WindevDocument d) => new
{
    d.Name, d.Kind, d.Header.WindevVersion, d.Header.ProductBuild, Sections = d.Sections.Count,
    DecodedBlocks = d.Sections.Sum(s => s.DecodedBlocks.Count), SourceBlocks = d.CodeBlocks.Count,
    SourceLines = d.CodeBlocks.Sum(b => b.Lines.Count),
    Tables = (d as WddWindevAnalysis)?.Tables.Count,
    Fields = (d as WddWindevAnalysis)?.Tables.Sum(t => t.Fields.Count),
    Elements = d switch { WdwWindevWindow w => w.Elements.Count, WdeWindevReport r => r.Elements.Count, _ => (int?)null },
    References = (d as WdpWindevProject)?.References.Count, d.Diagnostics
};
