using System.Buffers.Binary;
using WindevReader;

internal static class PersonneTests
{
    internal static void Run(string directory, Action<bool, string> check, Action<Action, string> reject, List<string> checks)
    {
        var fic = FicHfsqlDataFile.Open(Path.Combine(directory, "PERSONNE.fic"));
        var ndx = NdxHfsqlIndexFile.Open(Path.Combine(directory, "PERSONNE.ndx"));
        check(fic.Header.WindevVersion == 28 && fic.Header.ProductRevision == 0, "WINDEV 28 FIC profile");
        check(fic.Header.RawFlagsAt86 == 0x8000 && fic.Header.RawModeAt88 == 0, "Raw flags in caller-declared unprotected FIC");
        check(fic.HasSameIdentityAs(ndx), "New FIC/NDX matching physical identity");
        check(WindevFile.Open(ndx.FileName) is NdxHfsqlIndexFile, "WINDEV 28 generic dispatch without override");
        check(ndx.DirectoryOffset == 12896 && ndx.DeclaredIndexCount == 3, "WINDEV 28 terminal directory");
        check(ndx.IndexDescriptorOffsets.SequenceEqual(new[] {5034,9229,12836}), "Relocated descriptors");
        check(ndx.ReadIndexRegion(0).Length == 4195 && ndx.ReadIndexRegion(2).Length == 60, "Terminal regions are not complete logical indexes");
        ulong[] configurations = [135,5094,9289], pages = [1484,6323,10423], other = [2336,6771,10568];
        uint[] lengths = [852,448,145];
        for (int i = 0; i < 3; i++)
        {
            var d = ndx.IndexDescriptors[i];
            check(d.RawPointerAt28 == configurations[i] && d.RawPointerAt16 == pages[i] &&
                d.RawPointerAt36 == other[i] && d.RawLengthAt24 == lengths[i], "Descriptor pointers independently delimit regions");
            check(d.RawPointerAt16 + d.RawLengthAt24 == d.RawPointerAt36, "Observed first region exact coverage");
        }
        var fields = new HfsqlFixedField[] {
            new("EtatBrut",0,1,HfsqlFixedFieldType.UInt8), new("CompteurBrut",1,2,HfsqlFixedFieldType.UInt16),
            new("MotBrut",3,4,HfsqlFixedFieldType.UInt32), new("Identifiant",9,8,HfsqlFixedFieldType.Int64),
            new("TexteA",17,51,HfsqlFixedFieldType.Cp1252Z), new("TexteB",68,51,HfsqlFixedFieldType.Cp1252Z),
            new("Nombre",119,4,HfsqlFixedFieldType.Int32), new("DernierOctet",123,1,HfsqlFixedFieldType.UInt8) };
        var layout = new HfsqlFixedRecordLayout(1776,124,5,fields);
        var records = fic.ReadFixedRecords(layout);
        check(records.Count == 5 && records[^1].Offset + 124 == fic.RawData.Length, "All five physical slots cover exact EOF");
        long[] ids = [1,3,4,5,-1];
        for (int i = 0; i < 5; i++)
        {
            var r = records[i];
            check((long)r.Values["Identifiant"] == ids[i], "Physical identifier including all-ones sentinel");
            check((string)r.Values["TexteA"] == "Bill" && (string)r.Values["TexteB"] == "Gates", "Clear fixed text values");
            check((int)r.Values["Nombre"] == 18 && (byte)r.Values["DernierOctet"] == 0, "Numeric value and final raw byte");
            check(r.RawData.Span.SequenceEqual(fic.RawData.Span.Slice(1776+i*124,124)), "No transformed or skipped physical slot");
        }
        check(!fic.CanReadRecords && fic.PasswordStatus == HfsqlPasswordStatus.Unknown, "Explicit layout does not claim automatic discovery or password detection");
        var first = fields[0]; fields[0] = new("Changed",0,1,HfsqlFixedFieldType.UInt8);
        check(records[0].Values.ContainsKey(first.Name), "Caller field-array mutation cannot invalidate validated layout");
        reject(() => fic.ReadFixedRecords(layout with { DataOffset = -1 }), "Negative record position");
        reject(() => fic.ReadFixedRecords(layout with { RecordCount = int.MaxValue }), "Record count overflow");
        reject(() => fic.ReadFixedRecords(layout with { RecordByteLength = int.MaxValue }), "Record stride overflow");
        reject(() => fic.ReadFixedRecords(layout with { RecordCount = 6 }), "Truncated physical record");
        reject(() => fic.ReadFixedRecords(layout with { Fields = new[] {new HfsqlFixedField("bad",123,8,HfsqlFixedFieldType.Int64)} }), "Field past slot boundary");
        reject(() => fic.ReadFixedRecords(layout with { Fields = new[] {new HfsqlFixedField("bad",0,2,HfsqlFixedFieldType.Int64)} }), "Invalid integer width");
        reject(() => fic.ReadFixedRecords(layout with { Fields = new[] {first,first} }), "Duplicate field names");
        reject(() => fic.ReadFixedRecords(layout with { Fields = new[] {new HfsqlFixedField("bad",17,4,HfsqlFixedFieldType.Cp1252Z)} })[0].ToString(), "Unterminated text field");
        string temporary = Path.Combine(Path.GetTempPath(), "personne-tests-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            byte[] encodingFixture = fic.RawData.ToArray();
            encodingFixture[1776] = 0xC3; encodingFixture[1777] = 0xA9; encodingFixture[1778] = 0;
            File.WriteAllBytes(temporary, encodingFixture);
            var clear = FicHfsqlDataFile.Open(temporary);
            var encodingLayout = new HfsqlFixedRecordLayout(1776, 124, 1,
                new[] { new HfsqlFixedField("Text", 0, 3, HfsqlFixedFieldType.Cp1252Z) });
            check((string)clear.ReadFixedRecords(encodingLayout)[0].Values["Text"] == "Ã©", "Explicit CP1252 never guesses UTF-8");
            encodingFixture[1776] = 0xAC; encodingFixture[1777] = 0x20;
            encodingFixture[1778] = 0; encodingFixture[1779] = 0;
            File.WriteAllBytes(temporary, encodingFixture); clear = FicHfsqlDataFile.Open(temporary);
            encodingLayout = encodingLayout with { Fields = new[] {new HfsqlFixedField("Text",0,4,HfsqlFixedFieldType.Utf16LeZ)} };
            check((string)clear.ReadFixedRecords(encodingLayout)[0].Values["Text"] == "€", "Explicit UTF-16LE aligned terminator");
            encodingFixture[1776] = 0; encodingFixture[1777] = 0xD8;
            File.WriteAllBytes(temporary, encodingFixture); clear = FicHfsqlDataFile.Open(temporary);
            reject(() => clear.ReadFixedRecords(encodingLayout)[0].ToString(), "Unpaired UTF-16 surrogate rejected");
            var changed = ndx.RawData.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(5036),61);
            File.WriteAllBytes(temporary,changed); reject(() => NdxHfsqlIndexFile.Open(temporary), "Unsupported descriptor size");
            changed = fic.RawData.ToArray(); changed[14] = 29; File.WriteAllBytes(temporary,changed);
            reject(() => FicHfsqlDataFile.Open(temporary), "Version 29 is not silently accepted");
        }
        finally { File.Delete(temporary); }
        checks.Add("PERSONNE WINDEV 28: matching FIC/NDX, three relocated 60-byte descriptors, five clear physical 124-byte slots via explicit layout; values, sentinel and malformed-layout checks. Logical live/deleted state and automatic schema remain unknown.");
    }
}
