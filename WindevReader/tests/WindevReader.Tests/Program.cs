using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindevReader;

if (args.Length == 0) { Console.Error.WriteLine("Usage: dotnet run --project tests/WindevReader.Tests -- /path/to/Origamide [report.json]"); return 2; }
string root = Path.GetFullPath(args[0]); int assertions = 0; var checks = new List<string>();
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (WindevFormatException) { rejected = true; }
    Check(rejected, message);
}
try
{
    var schema = WddWindevAnalysis.Open(Path.Combine(root, "Origamide/Origamide.wdd"));
    Check(schema.Name == "Origamide" && schema.Header.WindevVersion == 31, "Analysis header/name");
    Check(schema.Generation == 24 && schema.Guid == "B4ADAD26761A4FF5AC4C73F84D467D4E", "Analysis identity/generation");
    Check(schema.Tables.Count == 6 && schema.Tables.Sum(t => t.Fields.Count) == 37, "Current schema sizes");
    using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "analysis.json")));
    var typeMap = new Dictionary<ushort, int> { [24] = 1, [11] = 11, [29] = 15, [14] = 14, [36] = 12, [37] = 27, [27] = 20, [25] = 19, [34] = 24, [2] = 2, [28] = 23, [26] = 22 };
    foreach (var tableReference in oracle.RootElement.GetProperty("tables").EnumerateArray())
    {
        string tableName = tableReference.GetProperty("name").GetString()!;
        var table = schema.Tables.Single(t => t.Name == tableName);
        Check(table.PhysicalName == tableReference.GetProperty("physicalName").GetString(), $"Physical name {tableName}");
        var fields = tableReference.GetProperty("columns").EnumerateArray().ToArray();
        Check(table.Fields.Count == fields.Length, $"Field count {tableName}");
        for (int i = 0; i < fields.Length; i++)
        {
            var actual = table.Fields[i]; var expected = fields[i];
            Check(actual.Name == expected.GetProperty("name").GetString(), $"Field name/order {tableName}:{i}");
            Check(actual.Size == int.Parse(expected.GetProperty("size").GetString()!), $"Field size {tableName}.{actual.Name}");
            Check(actual.Caption == expected.GetProperty("title").GetString(), $"Field caption {tableName}.{actual.Name}");
            Check(typeMap[actual.RawTypeCode] == int.Parse(expected.GetProperty("internalType").GetString()!), $"Field type {tableName}.{actual.Name}");
        }
    }
    checks.Add("Independent analysis.json oracle: all 37 names, types, sizes and captions match.");
    var early = WddWindevAnalysis.Open(Path.Combine(root, "Origamide/Origamide.ana/ANA00001/Origamide.wdd"));
    Check(early.Tables.Count == 1 && early.Tables[0].Fields.Count == 3, "First historical schema");
    var previous = WddWindevAnalysis.Open(Path.Combine(root, "Origamide/Origamide.ana/ANA00023/Origamide.wdd"));
    Check(previous.Tables.Sum(t => t.Fields.Count) == 36, "Previous historical schema");
    checks.Add("Historical analysis versions: first 1 table/3 fields; generation 23 has 36 fields.");
    var procedures = WdgWindevGlobalProcedures.Open(Path.Combine(root, "Origamide/ProcéduresGlobales.wdg"));
    Check(procedures.Procedures.Count == 9, "Procedure inventory");
    Check(procedures.Procedures.Single(p => p.Name == "RechargeJoursFeries").Source.Contains("JourFériéSupprimeTout()"), "Accent preservation in procedure source");
    Check(procedures.Procedures.Single(p => p.Name == "LitChaineFlags").Source.Contains("PROCÉDURE", StringComparison.OrdinalIgnoreCase), "Procedure source declaration");
    var bridge = WdgWindevGlobalProcedures.Open(Path.Combine(root, "bridge/ProcéduresGlobales.wdg"));
    Check(bridge.Header.WindevVersion == 28 && bridge.Procedures.Count == 2, "WINDEV 28 procedure events");
    Check(bridge.Procedures.Any(p => p.Name == "CréeFichierDescAnalyse" && p.Source.Contains("HListeRubrique")), "WINDEV 28 code text");
    checks.Add("Global procedures: 9 in Origamide and 2 in bridge; source accents and declaration text verified.");
    var window = WdwWindevWindow.Open(Path.Combine(root, "Origamide/FEN_Rapport1.wdw"));
    Check(window.Caption == "Rapport1" && window.Bounds?.Width == 354 && window.Bounds.Height == 126, "Window caption/geometry");
    Check(window.CodeBlocks.Any(b => b.OwnerName == "RempliCombo" && b.Source.Contains("PROC", StringComparison.OrdinalIgnoreCase)), "Local window procedure");
    var journal = WdwWindevWindow.Open(Path.Combine(root, "Origamide/FEN_Journal.wdw"));
    Check(journal.Caption == "Journal - Origamide" && journal.Bounds?.Width == 1177 && journal.Bounds.Height == 752, "Journal caption/geometry");
    Check(journal.Elements.Any(e => e.Name == "OPT_Marquer_comme_en_attente"), "Window menu element");
    var report = WdeWindevReport.Open(Path.Combine(root, "Origamide/ETAT_Test.wde"));
    Check(report.NoDataMessage == "Il n'y a pas de données à imprimer.", "Report no-data message");
    Check(report.Elements.Any(e => e.Name == "CORPS") && report.Elements.Any(e => e.Name == "NUMNBPAGES"), "Report blocks/controls");
    checks.Add("Window geometry/titles, local source, menu option and report blocks verified.");
    var project = WdpWindevProject.Open(Path.Combine(root, "Origamide/Origamide.wdp"));
    Check(project.References.Count == 27 && project.References.Any(r => r.Name == "FEN_Journal"), "Project reference directory");
    Check(project.References.Any(r => r.Name.EndsWith("Origamide.wdd")), "Project analysis reference");
    var bridgeProject = WdpWindevProject.Open(Path.Combine(root, "bridge/bridge.wdp"));
    Check(bridgeProject.References.Any(r => r.Name == "ProcéduresGlobales"), "Compressed WINDEV 28 project directory");
    checks.Add("Project configuration references verified, including compressed WINDEV 28 directory.");
    string tempDirectory = Path.Combine(Path.GetTempPath(), "windev-reader-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tempDirectory);
    try
    {
        string bad = Path.Combine(tempDirectory, "bad.wdd"); byte[] raw = schema.RawData.ToArray();
        foreach (int length in new[] { 0, 3, 84, 110, raw.Length - 1 })
        { File.WriteAllBytes(bad, raw[..length]); Reject(() => WddWindevAnalysis.Open(bad), "Truncated file rejection"); }
        byte[] changed = (byte[])raw.Clone(); changed[0] = 0; File.WriteAllBytes(bad, changed);
        Reject(() => WddWindevAnalysis.Open(bad), "Bad magic rejection");
        changed = (byte[])raw.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(81), uint.MaxValue); File.WriteAllBytes(bad, changed);
        Reject(() => WddWindevAnalysis.Open(bad), "Bad directory count rejection");
        changed = (byte[])raw.Clone(); changed[500] ^= 0x80; File.WriteAllBytes(bad, changed);
        Reject(() => WddWindevAnalysis.Open(bad), "Damaged encrypted payload rejection");
        Reject(() => WddWindevAnalysis.Open(Path.Combine(root, "Origamide/Origamide.wdd"), new() { MaxDecodedBlockBytes = 32 }), "Decompression budget rejection");
        Reject(() => WdwWindevWindow.Open(Path.Combine(root, "Origamide/ETAT_Test.wde")), "Wrong typed reader rejection");
        changed = (byte[])raw.Clone(); changed[14] = 99; File.WriteAllBytes(bad, changed);
        Reject(() => WddWindevAnalysis.Open(bad), "Untested version rejection");
    }
    finally { Directory.Delete(tempDirectory, true); }
    checks.Add("Malformed magic, truncation, directory counts, damaged payloads, type mismatch, untested versions and decompression limits rejected.");
    ExtensionTests.Run(root, Check, Reject, checks);
    if (args.Length > 2) PersonneTests.Run(args[2], Check, Reject, checks);
    if (args.Length > 3) ErpTests.Run(args[3], Check, Reject, checks);
    if (args.Length > 4) Hfsql31Tests.Run(args[4], root, Check, Reject, checks);
    var supported = new HashSet<string>([".wdp", ".wdw", ".wdg", ".wdd", ".wda", ".wde"], StringComparer.OrdinalIgnoreCase);
    var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => supported.Contains(Path.GetExtension(f))).ToArray();
    int blocks = 0, lines = 0;
    foreach (string file in files)
    {
        var doc = WindevDocument.Open(file); Check(doc.Sections.Sum(s => s.ByteLength) + doc.Header.PayloadOffset == doc.RawData.Length, "Corpus byte coverage");
        blocks += doc.Sections.Sum(s => s.DecodedBlocks.Count); lines += doc.CodeBlocks.Sum(b => b.Lines.Count);
    }
    Check(files.Length == 98, "Complete corpus count");
    checks.Add($"Complete corpus: {files.Length} documents, {blocks} decoded buffers, {lines} source lines.");
    var result = new { Passed = true, Assertions = assertions, Documents = files.Length, DecodedBuffers = blocks, SourceLines = lines, Checks = checks };
    string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }); Console.WriteLine(json);
    if (args.Length > 1) File.WriteAllText(args[1], json); return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
