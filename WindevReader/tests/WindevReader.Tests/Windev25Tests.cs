using System.Buffers.Binary;
using WindevReader;

internal static class Windev25Tests
{
    internal static void Run(string directory, Action<bool, string> check, Action<Action, string> reject, List<string> checks)
    {
        WdpWindevProject project = WdpWindevProject.Open(Path.Combine(directory, "Test25.wdp"));
        WdwWindevWindow window = WdwWindevWindow.Open(Path.Combine(directory, "FEN_test.wdw"));
        WddWindevAnalysis schema = WddWindevAnalysis.Open(Path.Combine(directory, "Test25.wdd"));
        FicHfsqlDataFile fic = FicHfsqlDataFile.Open(Path.Combine(directory, "Exe/PERSONNE.fic"));
        NdxHfsqlIndexFile ndx = NdxHfsqlIndexFile.Open(Path.Combine(directory, "Exe/PERSONNE.ndx"));
        MmoHfsqlMemoFile mmo = MmoHfsqlMemoFile.Open(Path.Combine(directory, "Exe/PERSONNE.mmo"));

        foreach (IWindevFile file in new IWindevFile[]
        {
            project,
            window,
            schema,
            fic,
            ndx,
            mmo
        }

        )
        {
            check(!file.Diagnostics.Any(diagnostic => diagnostic.Code == "UntestedVersion"), "Observed 25 family accepted");

            check(WindevFile.Open(file.FileName).GetType() == file.GetType(), "25 signature dispatch");
        }

        check(project.Header.SectionCount == 20 && project.Header.PayloadOffset == 345, "25 project directory");

        check(
            project.References.Count == 3 && project.References.Any(reference => reference.Name == "FEN_test"),
            "25 compressed configuration references");

        check(project.Sections[13].DecodedBlocks.Single().Data.Length == 3874, "25 compressed configuration length");

        check(project.CodeBlocks.Count == 11, "25 project code tag 14");

        check(window.CodeBlocks.Count == 36, "25 window source blocks");

        check(
            window.CodeBlocks.Any(codeBlock => codeBlock.OwnerName == "BTN_Créé" && codeBlock.Source.Contains("HAjoute(PERSONNE)")),
            "25 owner tag 0b and source text");

        check(
            window.Caption == "test" && window.Bounds == new WindevBounds(0, 0, 800, 600),
            "25 descriptor at 0 and caption displacement 506");

        check(
            window.PropertyBuffers[0].Strings[0].Offset == 4 && window.PropertyBuffers[0].Data.Length == 10611,
            "25 no extra root prefix");

        check(
            window.Preview?.Width == 150 && window.Preview.Height == 113 && window.Preview.BmpByteLength == 67866,
            "25 scaled BMP preview differs from window size");

        check(window.Preview!.GetRgba32().Length == 150 * 113 * 4, "25 BMP pixel extraction");

        check(
            schema.DecodedSchema.Length == 2171 && schema.Generation == 1 && schema.Tables.Count == 1,
            "25 WDD checksum and directories");

        check(schema.Tables[0].Offset == 662 && schema.DecodedSchema.Span[662] == 0x19, "25 table marker 19");

        check(
            schema.Tables[0].Fields.Select(field2 => field2.Name).SequenceEqual(new[] { "IDPERSONNE", "NOM", "PRENOM", "DESC" }),
            "25 schema field definitions");

        foreach (WindevDataField field3 in schema.Tables[0].Fields)
        {
            check(schema.DecodedSchema.Span[(int)field3.Offset] == 0x15, "25 field marker 15");
        }

        check(
            fic.HasSameIdentityAs(ndx) && fic.HasSameIdentityAs(mmo) && fic.FindTableDefinition(schema) == schema.Tables[0],
            "25 all companion identities");

        HfsqlFixedRecordLayout layout = fic.CreateFixedRecordLayout(schema, 1746, 1);
        HfsqlFixedRecord row = fic.ReadFixedRecords(layout)[0];

        check(
            layout.RecordByteLength == 127 && layout.Fields.Select(field4 => field4.Offset).SequenceEqual(new[] { 9, 17, 68, 119 }),
            "25 fixed layout independent of metadata shift");

        check(
            (long)row.Values["IDPERSONNE"] == 1 && ((string)row.Values["NOM"]).Length == 6 && ((string)row.Values["PRENOM"]).Length == 7,
            "25 clear record lengths");

        check((ulong)row.Values["DESC"] == 210, "25 absolute memo reference");

        HfsqlMemoBlock memo = mmo.ReadMemo(fic, 210);

        check(
            memo.PayloadOffset == 250 && memo.PayloadByteLength == 20 && memo.StoredRegionByteLength == 39,
            "25 simple memo block");

        check(memo.DecodeText(HfsqlMemoTextEncoding.Cp1252) == "Test en beurre carré", "25 memo explicit CP1252 accents");

        HfsqlFlatIndex page = ndx.ReadFlatIndex(0);

        check(
            ndx.IndexDescriptors.Count == 1 && page.Entries.Count == 1 && page.DeclaredEntryCount == 2,
            "25 flat index sentinel");

        check(
            page.Entries[0].KeyHex == "8000000000000001" && page.Entries[0].RawReference1 == 1 && page.Entries[0].RawReference2 == 1,
            "25 index agrees with FIC identifier");

        reject(() => fic.CreateFixedRecordLayout(schema, 1747, 1), "25 incorrect data boundary refused");

        reject(() => mmo.ReadMemo(211), "25 incorrect memo boundary refused");

        string temporaryBuffer = Path.Combine(Path.GetTempPath(), "windev25-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            byte[] bytes = project.RawData.ToArray();

            bytes[14] = 26;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => WdpWindevProject.Open(temporaryBuffer), "26 source remains unvalidated");

            bytes = fic.RawData.ToArray();

            bytes[14] = 26;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => FicHfsqlDataFile.Open(temporaryBuffer), "26 physical remains unvalidated");

            bytes = ndx.RawData.ToArray();

            bytes[248] = 0;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => NdxHfsqlIndexFile.Open(temporaryBuffer).ReadFlatIndex(0), "25 invalid sentinel refused");

            bytes = mmo.RawData.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(246), 21);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "25 contradictory memo length refused");
        }
        finally
        {
            File.Delete(temporaryBuffer);
        }

        checks.Add("WINDEV 25: six supplied families, compressed WDP configuration, 11 project and 36 window code blocks (event tag 14, owner tag 0b), WDW root descriptor at zero/caption +506 and scaled 150x113 BMP, WDD tags 19/15, matching 127-byte FIC slot at 1746 with memo 210, simple CP1252 MMO and flat NDX sentinel. Versions 26/27 and other unprovided families remain unvalidated.");
    }
}
