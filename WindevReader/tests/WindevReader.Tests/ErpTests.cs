using System.Buffers.Binary;
using WindevReader;

internal static class ErpTests
{
    internal static void Run(string directory, Action<bool,string> check, Action<Action,string> reject, List<string> checks)
    {
        var analysis = WddWindevAnalysis.Open(Path.Combine(directory,"ERP.wdd"));
        var source = WdaWindevAnalysisSource.Open(Path.Combine(directory,"ERP.wda"));
        var fic = FicHfsqlDataFile.Open(Path.Combine(directory,"PERSONNE.fic"));
        check(analysis.Header.WindevVersion == 28 && analysis.Generation == 3, "ERP WDD 28 generation 3");
        check(analysis.DecodedSchema.Length == 6487 && analysis.Tables.Count == 2, "ERP checksum-valid decoded schema");
        check(analysis.Tables.Sum(t => t.Fields.Count) == 8, "ERP eight fields");
        var table = fic.FindTableDefinition(analysis);
        check(table?.Name == "PERSONNE" && fic.MatchesAnalysis(analysis), "WDD/FIC table and analysis GUID linkage");
        check(table!.RawRecordByteLength == 124, "Schema confirms physical stride 124");
        check(table.Fields.Select(f=>f.Name).SequenceEqual(new[]{"IDPERSONNE","NOM","PRENOM","AGE1","FLAGADMIN"}), "Schema corrects field working names/order");
        check(table.Fields.Select(f=>f.StorageSize).SequenceEqual(new ushort[]{8,51,51,4,1}), "Physical field sizes");
        check(table.Fields[1].Size == 50 && table.Fields[1].StorageSize == 51, "Text declaration versus physical storage");
        check(table.Fields[3].RawTypeCode == 5 && table.Fields[3].Type == "Int32", "WINDEV 28 int32 type");
        check(analysis.DeclaredRelationCount == 1 && analysis.Relations.Count == 1, "First non-empty decoded relation");
        var relation = analysis.Relations.Single();
        check(relation.SourceTable == "PERSONNE" && relation.TargetTable == "PERSONNE_MESSAGE", "Relation endpoints");
        check(relation.SourceField == "IDPERSONNE" && relation.TargetFieldId == 7 && relation.SourceFieldId == 1, "Relation field identities");
        check(relation.RawFlags.SequenceEqual(new ushort[]{0,65535,1,1}) && relation.RawTrailer == 48, "Unassigned raw relation metadata");
        check(!analysis.Diagnostics.Any(d=>d.Code=="UndecodedRelations") && analysis.Diagnostics.Any(d=>d.Code=="PartialRelationProperties"), "Accurate partial relation diagnostics");
        var layout = fic.CreateFixedRecordLayout(analysis,1776,5);
        check(layout.RecordByteLength == 124 && layout.Fields.Select(f=>f.Offset).SequenceEqual(new[]{9,17,68,119,123}), "Schema-derived fixed field offsets");
        var rows = fic.ReadFixedRecords(layout);
        check(rows.All(r=>(int)r.Values["AGE1"] == 18 && !(bool)r.Values["FLAGADMIN"]), "Named integer and bool values");
        check((string)rows[0].Values["NOM"] == "Bill" && (string)rows[0].Values["PRENOM"] == "Gates", "Names follow schema, not inferred human conventions");
        var block = source.Sections[0].DecodedBlocks.Single();
        check(source.RawData.Length == 10464 && block.Data.Length == 7188, "WINDEV 28 WDA logical block");
        check(BinaryPrimitives.ReadUInt32LittleEndian(block.Data.Span[475..]) == 124, "WDA independently repeats physical stride");
        check(block.Data.Span[571..604].SequenceEqual(System.Text.Encoding.ASCII.GetBytes(table.Guid+"\0")), "WDA/WDD physical table identity");
        string temporary = Path.Combine(Path.GetTempPath(),"erp-tests-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            byte[] changed = fic.RawData.ToArray();changed[20] = changed[20] == 'A' ? (byte)'B' : (byte)'A';
            File.WriteAllBytes(temporary,changed);
            var foreign = FicHfsqlDataFile.Open(temporary);
            reject(()=>foreign.CreateFixedRecordLayout(analysis,1776,5),"Schema mismatch is rejected before field decoding");
            changed = fic.RawData.ToArray(); changed[1899] = 1; File.WriteAllBytes(temporary,changed);
            var synthetic = FicHfsqlDataFile.Open(temporary);
            check((bool)synthetic.ReadFixedRecords(synthetic.CreateFixedRecordLayout(analysis,1776,5))[0].Values["FLAGADMIN"],"Boolean true in synthetic clear slot");
            changed[1899]=2;File.WriteAllBytes(temporary,changed); synthetic = FicHfsqlDataFile.Open(temporary);
            reject(()=>synthetic.ReadFixedRecords(synthetic.CreateFixedRecordLayout(analysis,1776,5))[0].ToString(),"Invalid physical bool rejected");
        }
        finally {File.Delete(temporary);}
        reject(()=>fic.CreateFixedRecordLayout(analysis,1776,6),"Schema-assisted range validation");
        checks.Add("ERP 28: WDD checksum, two tables/eight fields, physical GUID links, WDA corroboration, schema-assisted PERSONNE layout and first relation decoded; state/cardinality semantics remain partial.");
    }
}
