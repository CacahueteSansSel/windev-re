using System.Buffers.Binary;
using WindevReader;

internal static class Windev24Tests
{
    internal static void Run(string directory, Action<bool, string> check, Action<Action, string> reject, List<string> checks)
    {
        WdpWindevProject project = WdpWindevProject.Open(Path.Combine(directory, "Test24.wdp"));
        WdwWindevWindow window = WdwWindevWindow.Open(Path.Combine(directory, "FEN_test.wdw"));
        WddWindevAnalysis schema = WddWindevAnalysis.Open(Path.Combine(directory, "Test24.wdd"));
        FicHfsqlDataFile fic = FicHfsqlDataFile.Open(Path.Combine(directory, "Exe/PERSONNE.fic"));
        NdxHfsqlIndexFile ndx = NdxHfsqlIndexFile.Open(Path.Combine(directory, "Exe/PERSONNE.ndx"));
        MmoHfsqlMemoFile mmo = MmoHfsqlMemoFile.Open(Path.Combine(directory, "Exe/PERSONNE.mmo"));

        check(window.Elements.Single(element => element.Name == "SAI_Nom").Bounds == new WindevBounds(6, 6, 220, 22), "Version 24 control geometry corroborates the common header");
        check(window.Elements.Single(element => element.Name == "SAI_Nom").Caption == "Nom", "Version 24 input caption corroborates the common field");
        check(window.Elements.Single(element => element.Name == "BTN_Créé").Caption == "créé", "Version 24 button caption uses its version-specific position");

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
            check(!file.Diagnostics.Any(diagnostic => diagnostic.Code == "UntestedVersion"), "Observed 24 family accepted");

            check(WindevFile.Open(file.FileName).GetType() == file.GetType(), "24 signature dispatch");
        }

        check(project.Header.SectionCount == 20 && project.Header.PayloadOffset == 345, "24 project directory");

        check(
            project.References.Count == 3 && project.References.Any(reference => reference.Name == "FEN_test"),
            "24 compressed configuration references");

        check(project.Sections[13].DecodedBlocks.Single().Data.Length == 3332, "24 compressed configuration length");

        check(project.CodeBlocks.Count == 10, "24 project code tag 14");

        check(window.CodeBlocks.Count == 36, "24 window source blocks");

        check(
            window.CodeBlocks.Any(codeBlock => codeBlock.OwnerName == "BTN_Créé" && codeBlock.Source.Contains("HAjoute(PERSONNE)")),
            "24 owner tag 0b and source text");

        check(
            window.Caption == "test" && window.Bounds == new WindevBounds(0, 0, 800, 600),
            "24 descriptor at 0 and caption displacement 506");

        check(
            window.PropertyBuffers[0].Strings[0].Offset == 4 && window.PropertyBuffers[0].Data.Length == 10147,
            "24 no extra root prefix");

        check(
            window.Preview?.Width == 150 && window.Preview.Height == 113 && window.Preview.BmpByteLength == 67866,
            "24 scaled BMP preview differs from window size");

        check(window.Preview!.GetRgba32().Length == 150 * 113 * 4, "24 BMP pixel extraction");

        check(
            schema.DecodedSchema.Length == 2153 && schema.Generation == 1 && schema.Tables.Count == 1,
            "24 WDD checksum and directories");

        check(schema.Tables[0].Offset == 662 && schema.DecodedSchema.Span[662] == 0x19, "24 table marker 19");

        check(
            schema.Tables[0].Fields.Select(field2 => field2.Name).SequenceEqual(new[] { "IDPERSONNE", "PRENOM", "NOM", "DESC" }),
            "24 schema field definitions");

        foreach (WindevDataField field3 in schema.Tables[0].Fields)
        {
            check(schema.DecodedSchema.Span[(int)field3.Offset] == 0x15, "24 field marker 15");
        }

        check(
            fic.HasSameIdentityAs(ndx) && fic.HasSameIdentityAs(mmo) && fic.FindTableDefinition(schema) == schema.Tables[0],
            "24 all companion identities");

        HfsqlFixedRecordLayout layout = fic.CreateFixedRecordLayout(schema, 1734, 1);
        HfsqlFixedRecord row = fic.ReadFixedRecords(layout)[0];

        check(
            layout.RecordByteLength == 127 && layout.Fields.Select(field4 => field4.Offset).SequenceEqual(new[] { 9, 17, 68, 119 }),
            "24 fixed layout independent of metadata shift");

        check(
            (long)row.Values["IDPERSONNE"] == 1 && ((string)row.Values["NOM"]).Length == 4 && ((string)row.Values["PRENOM"]).Length == 3,
            "24 schema order PRENOM before NOM and clear record lengths");

        check((ulong)row.Values["DESC"] == 210, "24 absolute memo reference");

        HfsqlMemoBlock memo = mmo.ReadMemo(fic, 210);

        check(
            memo.PayloadOffset == 250 && memo.PayloadByteLength == 4 && memo.StoredRegionByteLength == 23,
            "24 simple memo block");

        check(
            memo.DecodeText(HfsqlMemoTextEncoding.Cp1252) == "Test",
            "24 ASCII-compatible memo with caller-selected CP1252");

        HfsqlFlatIndex page = ndx.ReadFlatIndex(0);

        check(
            ndx.IndexDescriptors.Count == 1 && page.Entries.Count == 1 && page.DeclaredEntryCount == 2,
            "24 flat index sentinel");

        check(
            page.Entries[0].KeyHex == "8000000000000001" && page.Entries[0].RawReference1 == 1 && page.Entries[0].RawReference2 == 1,
            "24 index agrees with FIC identifier");

        reject(() => fic.CreateFixedRecordLayout(schema, 1735, 1), "24 incorrect data boundary refused");

        reject(() => mmo.ReadMemo(211), "24 incorrect memo boundary refused");

        string temporaryBuffer = Path.Combine(Path.GetTempPath(), "windev24-" + Guid.NewGuid().ToString("N") + ".bin");

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

            reject(() => NdxHfsqlIndexFile.Open(temporaryBuffer).ReadFlatIndex(0), "24 invalid sentinel refused");

            bytes = mmo.RawData.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(246), 21);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "24 contradictory memo length refused");
        }
        finally
        {
            File.Delete(temporaryBuffer);
        }

        checks.Add("WINDEV 24: six supplied families, compressed WDP configuration, 10 project and 36 window code blocks (event tag 14, owner tag 0b), WDW root descriptor at zero/caption +506 and scaled 150x113 BMP, WDD tags 19/15, matching 127-byte FIC slot at 1734 with memo 210, simple CP1252 MMO and flat NDX sentinel. Versions 26/27 and other unprovided families remain unvalidated.");
    }
}
