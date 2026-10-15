using System.Text.Json;
using System.Text.Json.Serialization;
using WindevReader;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
try
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: WindevReader.Cli index-entries NDX INDEX [OUTPUT] | layout FIC WDD DATA_OFFSET COUNT [OUTPUT] | records FIC LAYOUT.json [OUTPUT] | inspect FILE | json FILE [OUTPUT] | code FILE [OUTPUT] | preview FILE OUTPUT.bmp | extract-resource WDL INDEX OUTPUT | corpus DIRECTORY [OUTPUT]");
        return 2;
    }
    if (args[0] == "corpus")
    {
        var reports = new List<object>(); bool failed = false;
        var supported = new HashSet<string>([".wdp", ".wdw", ".wdg", ".wdd", ".wda", ".wde", ".fic", ".ndx", ".mmo", ".wdl"], StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Where(f => supported.Contains(Path.GetExtension(f)) || (Path.GetFileName(f).Contains(".wdl.", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(f) != ".wdu")).Order())
        {
            try { var doc = WindevFile.Open(file); reports.Add(new { File = Path.GetRelativePath(args[1], file), Success = true, Summary = Summary(doc) }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { failed = true; reports.Add(new { File = Path.GetRelativePath(args[1], file), Success = false, Error = error.Message }); }
        }
        Write(JsonSerializer.Serialize(reports, jsonOptions), args.ElementAtOrDefault(2)); return failed ? 1 : 0;
    }
    var document = WindevFile.Open(args[1]);
    if (args[0] == "index-entries")
    {
        if (args.Length is < 3 or > 4 || document is not NdxHfsqlIndexFile index || !int.TryParse(args[2], out int number))
            throw new ArgumentException("index-entries requires an NDX, index number and optional output");
        Write(JsonSerializer.Serialize(index.ReadFlatIndex(number), jsonOptions), args.ElementAtOrDefault(3));
        return 0;
    }
    if (args[0] == "layout")
    {
        if (args.Length is < 5 or > 6 || document is not FicHfsqlDataFile fic ||
            !int.TryParse(args[3], out int offset) || !int.TryParse(args[4], out int count))
            throw new ArgumentException("layout requires a FIC, WDD, decimal data offset, physical slot count, and optional output");
        var analysis = WddWindevAnalysis.Open(args[2]);
        Write(JsonSerializer.Serialize(fic.CreateFixedRecordLayout(analysis, offset, count), jsonOptions), args.ElementAtOrDefault(5));
        return 0;
    }
    if (args[0] == "records")
    {
        if (args.Length is < 3 or > 4 || document is not FicHfsqlDataFile fic)
            throw new ArgumentException("records requires a FIC, an explicit layout JSON, and an optional output");
        var layout = JsonSerializer.Deserialize<HfsqlFixedRecordLayout>(File.ReadAllText(args[2]), jsonOptions)
            ?? throw new ArgumentException("Missing fixed record layout");
        Write(JsonSerializer.Serialize(fic.ReadFixedRecords(layout), jsonOptions), args.ElementAtOrDefault(3));
        return 0;
    }
    if (args[0] == "preview")
    {
        if (args.Length != 3) throw new ArgumentException("preview requires an input WDW and an output BMP path");
        if (document is not WdwWindevWindow { Preview: { } preview }) throw new UnsupportedWindevFormatException("No supported BMP window preview");
        preview.SaveBmp(args[2]); return 0;
    }
    if (args[0] == "extract-resource")
    {
        if (args.Length != 4 || !int.TryParse(args[2], out int index)) throw new ArgumentException("extract-resource requires WDL, entry index and output path");
        if (document is not WdlWindevLibrary library || index < 0 || index >= library.Entries.Count)
            throw new ArgumentException("Not a WDL or invalid entry index");
        File.WriteAllBytes(args[3], library.ReadResource(library.Entries[index])); return 0;
    }
    string result = args[0] switch
    {
        "inspect" => JsonSerializer.Serialize(Summary(document), jsonOptions),
        "json" => JsonSerializer.Serialize(document, document.GetType(), jsonOptions),
        "code" => document is WindevDocument pcs ? string.Join("\n\n", pcs.CodeBlocks.Select(b => $"// {b.OwnerName} — événement {b.EventId}, langue {b.LanguageId}\n{b.Source}")) : throw new UnsupportedWindevFormatException("Source extraction is only available for source PCS documents"),
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
static object Summary(IWindevFile file)
{
    if (file is WdlWindevLibrary library) return new
    {
        library.Name, Kind = "CompiledLibrary", library.ApplicationVersion, library.Header,
        Entries = library.Entries.Count, Resources = library.Entries.Count(e => e.IsResource), library.Diagnostics
    };
    if (file is NdxHfsqlIndexFile index) return new
    { index.Name, index.Kind, index.Header, index.DeclaredIndexCount, index.DirectoryOffset,
        index.IndexDescriptors, index.PasswordStatus, index.CanReadRecords, index.Diagnostics };
    if (file is HfsqlPhysicalFile physical) return new
    { physical.Name, physical.Kind, physical.Header, physical.PasswordStatus, physical.CanReadRecords, physical.Diagnostics };
    var d = (WindevDocument)file;
    return new
{
    d.Name, d.Kind, d.Header.WindevVersion, d.Header.ProductBuild, Sections = d.Sections.Count,
    DecodedBlocks = d.Sections.Sum(s => s.DecodedBlocks.Count), SourceBlocks = d.CodeBlocks.Count,
    SourceLines = d.CodeBlocks.Sum(b => b.Lines.Count),
    Tables = (d as WddWindevAnalysis)?.Tables.Count,
    Fields = (d as WddWindevAnalysis)?.Tables.Sum(t => t.Fields.Count),
    Elements = d switch { WdwWindevWindow w => w.Elements.Count, WdeWindevReport r => r.Elements.Count, _ => (int?)null },
    References = (d as WdpWindevProject)?.References.Count,
    Preview = (d as WdwWindevWindow)?.Preview, d.Diagnostics
};
}
