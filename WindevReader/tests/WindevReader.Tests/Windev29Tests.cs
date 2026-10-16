using System.Buffers.Binary;
using WindevReader;

internal static class Windev29Tests
{
    internal static void Run(
        string directory,
        string originalRoot,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WdpWindevProject project = WdpWindevProject.Open(Path.Combine(directory, "Test29.wdp"));
        WdwWindevWindow window = WdwWindevWindow.Open(Path.Combine(directory, "FEN_test.wdw"));
        WddWindevAnalysis schema = WddWindevAnalysis.Open(Path.Combine(directory, "Test29.wdd"));
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
            check(
                !file.Diagnostics.Any(document => document.Code == "UntestedVersion"),
                "Observed 29 families accepted without version override");

            check(WindevFile.Open(file.FileName).GetType() == file.GetType(), "29 signature dispatch matches typed reader");
        }

        check(project.Header.SectionCount == 20 && project.Header.PayloadOffset == 345, "WDP 29 envelope");

        check(
            project.References.Count == 3 && project.References.Any(report => report.Name == "FEN_test"),
            "WDP 29 build references");

        check(
            window.Header.SectionCount == 12 && window.PropertyBuffers[0].Data.Length == 13982,
            "WDW 29 property buffer");

        check(
            window.Caption == "test" && window.Bounds == new WindevBounds(0, 0, 800, 600),
            "WDW 29 descriptor 607 geometry and caption displacement");

        check(
            window.Preview?.Width == 800 && window.Preview.Height == 600 && window.Preview.BmpByteLength == 1920066,
            "Independent BMP 29 geometry");

        check(
            window.CodeBlocks.Any(codeBlock => codeBlock.Source.Contains("Hajoute(PERSONNE)", StringComparison.OrdinalIgnoreCase)) && window.CodeBlocks.Any(codeBlock2 => codeBlock2.Source.Contains("HLitPremier(PERSONNE)", StringComparison.OrdinalIgnoreCase)),
            "29 source read/write memo events");

        check(window.Elements.Any(entry => entry.Name == "SAI_Desc"), "29 memo input control identified");

        check(
            schema.Generation == 1 && schema.DecodedSchema.Length == 2287 && schema.Tables.Count == 1,
            "29 WDD checksum and table tag 21");

        check(
            schema.Tables[0].Fields.Select(field => field.Name).SequenceEqual(new[] { "IDPERSONNE", "NOM", "PRENOM", "DESC" }),
            "29 field tag 17 and definitions");

        check(
            schema.Tables[0].RawRecordByteLength == 127 && schema.Tables[0].Fields[^1].Type == "TextMemo",
            "29 slot length and memo schema");

        check(
            fic.HasSameIdentityAs(ndx) && fic.HasSameIdentityAs(mmo) && fic.FindTableDefinition(schema) == schema.Tables[0],
            "29 all physical companions match WDD");

        HfsqlFixedRecordLayout layout = fic.CreateFixedRecordLayout(schema, 1730, 1);

        check(
            layout.Fields.Select(field2 => field2.Offset).SequenceEqual(new[] { 9, 17, 68, 119 }),
            "29 fixed values plus memo reference offsets");

        HfsqlFixedRecord row = fic.ReadFixedRecords(layout)[0];

        check(
            (long)row.Values["IDPERSONNE"] == 1 && (string)row.Values["NOM"] == "John" && (string)row.Values["PRENOM"] == "Doe",
            "29 physical values");

        check((ulong)row.Values["DESC"] == 210, "29 FIC memo offset");

        HfsqlMemoBlock memo = mmo.ReadMemo(fic, (ulong)row.Values["DESC"]);

        check(
            memo.Offset == 210 && memo.PayloadOffset == 250 && memo.StoredRegionByteLength == 39 && memo.PayloadByteLength == 20,
            "MMO 29 outer/control header and lengths");

        check(
            memo.DecodeText(HfsqlMemoTextEncoding.Cp1252) == "Le mec par défaut là",
            "MMO 29 explicit CP1252 with accents and explicit length");

        HfsqlFlatIndex page = ndx.ReadFlatIndex(0);

        check(
            page.Entries.Count == 1 && page.DeclaredEntryCount == 2 && page.Entries[0].KeyHex == "8000000000000001",
            "NDX 29 flat page and sentinel");

        check(page.Entries[0].RawReference1 == 1 && page.Entries[0].RawReference2 == 1, "29 flat raw references");

        MmoHfsqlMemoFile oldMemo = MmoHfsqlMemoFile.Open(Path.Combine(originalRoot, "Origamide/Taches/Tache.mmo"));

        check(
            oldMemo.ReadMemo(210).PayloadByteLength == 26 && oldMemo.ReadMemo(210).DecodeText(HfsqlMemoTextEncoding.Utf16Le) == "// TODO: DOCX",
            "31 single-block memo independently matches new header layout");

        check(
            oldMemo.ReadMemo(594).PayloadByteLength == 100 && oldMemo.ReadMemo(594).DecodeText(HfsqlMemoTextEncoding.Utf16Le).StartsWith("0;2;FEN_Journal", StringComparison.Ordinal),
            "Second 31 memo block and UTF16 payload");

        reject(() => oldMemo.ReadMemo(338), "Non-memo region is not decoded as text by scanning");

        reject(() => oldMemo.ReadMemo(fic, 210), "Wrong FIC/MMO association refused");

        reject(() => mmo.ReadMemo(0), "Memo reference into header refused");

        reject(() => mmo.ReadMemo(ulong.MaxValue), "Overflowing memo reference refused");

        reject(
            () => MmoHfsqlMemoFile.Open(mmo.FileName, new WindevOpenOptions { MaxDecodedBlockBytes = 10 }).ReadMemo(210),
            "Memo block budget");

        reject(
            () => MmoHfsqlMemoFile.Open(mmo.FileName, new WindevOpenOptions { MaxStringBytes = 10 }).ReadMemo(210).DecodeText(HfsqlMemoTextEncoding.Cp1252),
            "Memo text decoding budget");

        string temporaryBuffer = Path.Combine(Path.GetTempPath(), "windev29-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            byte[] bytes = mmo.RawData.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), 40);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "Contradictory memo lengths");

            bytes = mmo.RawData.ToArray();

            bytes[220] = 1;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "Unsupported memo mode");

            bytes = mmo.RawData.ToArray();

            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(221), 338);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "Memo chains explicitly refused");

            bytes = mmo.RawData.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(212), 100019);

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), 100019);

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(246), 100000);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210), "Truncated memo payload");

            bytes = mmo.RawData.ToArray();

            bytes[250] = 0;

            File.WriteAllBytes(temporaryBuffer, bytes);

            check(
                MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210).DecodeText(HfsqlMemoTextEncoding.Cp1252)[0] == '\0',
                "Memo uses explicit length and preserves internal zero bytes");

            bytes = mmo.RawData.ToArray();

            bytes[250] = 0;

            bytes[251] = 0xD8;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(
                () => MmoHfsqlMemoFile.Open(temporaryBuffer).ReadMemo(210).DecodeText(HfsqlMemoTextEncoding.Utf16Le),
                "Unpaired memo UTF16 surrogate rejected");

            bytes = fic.RawData.ToArray();

            bytes[14] = 30;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => FicHfsqlDataFile.Open(temporaryBuffer), "Version 30 remains untested");

            bytes = project.RawData.ToArray();

            bytes[14] = 30;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => WdpWindevProject.Open(temporaryBuffer), "Source version 30 remains untested");
        }
        finally
        {
            File.Delete(temporaryBuffer);
        }

        checks.Add("WINDEV 29: WDP references, WDW descriptor 607/title/geometry/BMP and source, WDD table/field tags 21/17 and checksum, matching FIC/MMO/NDX, 127-byte row, 210-byte memo reference, bounded uncompressed memo block with explicit CP1252; same block structure corroborated by two UTF16LE memos in existing 31 file. Chains/compression/allocation and automatic encoding remain unsupported.");
    }
}
