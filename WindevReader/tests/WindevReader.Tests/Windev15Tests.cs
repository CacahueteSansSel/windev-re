using System.Buffers.Binary;
using WindevReader;

internal static class Windev15Tests
{
    internal static void Run(
        string directory,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WdpWindevProject project = WdpWindevProject.Open(Path.Combine(directory, "Test15.WDP"));
        WdwWindevWindow window = WdwWindevWindow.Open(Path.Combine(directory, "FEN_Fenetre1.wdw"));
        WddWindevAnalysis analysis = WddWindevAnalysis.Open(Path.Combine(directory, "Test15.wdd"));
        FicHfsqlDataFile data = FicHfsqlDataFile.Open(Path.Combine(directory, "Exe/PERSONNE.FIC"));
        NdxHfsqlIndexFile index = NdxHfsqlIndexFile.Open(Path.Combine(directory, "Exe/PERSONNE.ndx"));
        MmoHfsqlMemoFile memos = MmoHfsqlMemoFile.Open(Path.Combine(directory, "Exe/PERSONNE.mmo"));
        IWindevFile[] files = [project, window, analysis, data, index, memos];

        foreach (IWindevFile file in files)
        {
            check(!file.Diagnostics.Any(diagnostic => diagnostic.Code == "UntestedVersion"), "Observed version 15 family accepted");
            check(WindevFile.Open(file.FileName).GetType() == file.GetType(), "Version 15 signature dispatch with uppercase extensions");
        }

        check(project.Name == "Test15", "Legacy project identity is a zero-terminated string");
        check(project.Header.SectionCount == 19 && project.Header.PayloadOffset == 332, "Version 15 project envelope");
        check(project.CodeBlocks.Count == 6 && project.CodeBlocks.Sum(block => block.Lines.Count) == 12, "Version 15 project events and empty lines");
        check(project.References.Count == 0 && project.Diagnostics.Any(diagnostic => diagnostic.Code == "NoConfigurationDirectory"), "Unsupported legacy project references remain explicitly unknown");
        check(window.Name == "FEN_Fenêtre1", "Legacy embedded name preserves CP1252 accents independently of filename");
        check(window.Caption == "Titre de la fenêtre", "Legacy unframed caption at first-string-end plus 469");
        check(window.Bounds == new WindevBounds(0, 0, 800, 600), "Version 15 root geometry");
        check(window.Preview?.Width == 150 && window.Preview.Height == 113 && window.Preview.BmpByteLength == 67866, "Version 15 thumbnail is a standard scaled BMP");
        check(window.CodeBlocks.Count == 23 && window.CodeBlocks.Sum(block => block.Lines.Count) == 57, "Version 15 event tag 10 and length-prefixed source lines");
        check(window.CodeBlocks.Any(block => block.OwnerName == "BTN_Sauve" && block.Offset == 6391 && block.Source.Contains("HAjoute(PERSONNE)")), "Legacy length-prefixed owner and source association");
        check(window.CodeBlocks.First().Lines.Select(line => line.Offset).SequenceEqual(new int[] { 54, 69, 84 }), "Legacy empty source lines occupy fifteen bytes");
        check(window.CodeBlocks.First().Lines.All(line => line.Author == "GD"), "Legacy author byte lengths and four-byte timestamps");
        check(window.Elements.Any(element => element.Name == "SAI_Desc") && window.Elements.Any(element => element.Name == "BTN_Ouvre"), "Legacy property candidate strings expose named controls");
        check(window.Diagnostics.Any(diagnostic => diagnostic.Code == "HeuristicLegacyStrings"), "Unframed property candidates are not presented as fully mapped properties");
        check(analysis.Generation == 1 && analysis.DecodedSchema.Length == 1892, "Version 15 schema checksum");
        check(analysis.DecodedSchema.Span[192] == 4 && analysis.DecodedSchema.Span[586] == 0x12, "Version 15 analysis and table tags");
        check(analysis.Tables.Count == 1 && analysis.Tables[0].Fields.Count == 4, "Version 15 table and field directories");
        check(analysis.Tables[0].Fields.Select(field => field.Name).SequenceEqual(new string[] { "IDPERSONNE", "NOM", "PRENOM", "DESC" }), "Version 15 schema field order");

        foreach (WindevDataField field in analysis.Tables[0].Fields)
        {
            check(analysis.DecodedSchema.Span[field.Offset] == 0x10, "Version 15 field tag");
        }

        check(analysis.Tables[0].Fields[0].RawTypeCode == 38 && analysis.Tables[0].Fields[0].Type == "AutomaticIdentifier32", "Four-byte automatic identifier has distinct schema code 38");
        check(analysis.Tables[0].RawRecordByteLength == 123, "Version 15 record stride");
        check(data.HasSameIdentityAs(index) && data.HasSameIdentityAs(memos) && data.FindTableDefinition(analysis) == analysis.Tables[0], "All version 15 companion identities agree");

        HfsqlFixedRecordLayout layout = data.CreateFixedRecordLayout(analysis, 1688, 1);
        HfsqlFixedRecord record = data.ReadFixedRecords(layout)[0];

        check(layout.RecordByteLength == 123 && layout.Fields.Select(field => field.Offset).SequenceEqual(new int[] { 9, 13, 64, 115 }), "Four-byte identifier shifts subsequent physical fields");
        check(record.Values["IDPERSONNE"] is int identifier && identifier == 1, "Version 15 identifier uses explicit Int32 interpretation");
        check((string)record.Values["NOM"] == "JOHN" && (string)record.Values["PRENOM"] == "Doe", "Values retain their schema labels without human-name correction");
        check((ulong)record.Values["DESC"] == 210, "Version 15 memo reference remains eight bytes");

        HfsqlMemoBlock memo = memos.ReadMemo(data, 210);
        HfsqlFlatIndex page = index.ReadFlatIndex(0);

        check(memo.PayloadOffset == 250 && memo.PayloadByteLength == 46 && memo.StoredRegionByteLength == 65, "Version 15 simple memo framing");
        check(memo.DecodeText(HfsqlMemoTextEncoding.Cp1252) == "J'ai inversé nom et prénom mais bon t'inquiète", "Legacy memo text preserves CP1252 accents");
        check(page.KeyByteLength == 4 && page.Entries.Count == 1 && page.TerminalSentinelOffset == 244, "Version 15 flat index uses a four-byte key and twenty-byte entries");
        check(page.Entries[0].KeyHex == "80000001" && page.Entries[0].RawReference1 == 1 && page.Entries[0].RawReference2 == 1, "Version 15 key and raw references agree with the FIC");
        reject(() => data.CreateFixedRecordLayout(analysis, 1689, 1), "Wrong version 15 physical boundary refused");
        reject(() => memos.ReadMemo(0), "Header is not a memo block");
        reject(() => MmoHfsqlMemoFile.Open(memos.FileName, new WindevOpenOptions { MaxStringBytes = 20 }).ReadMemo(210).DecodeText(HfsqlMemoTextEncoding.Cp1252), "Legacy memo text budget remains enforced");
        reject(() => WdwWindevWindow.Open(window.FileName, new WindevOpenOptions { MaxStringBytes = 4 }), "Legacy identity string budget remains enforced");

        string temporaryFile = Path.Combine(Path.GetTempPath(), "windev15-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            byte[] changed = index.RawData.ToArray();

            changed[244] = 0;
            File.WriteAllBytes(temporaryFile, changed);
            reject(() => NdxHfsqlIndexFile.Open(temporaryFile).ReadFlatIndex(0), "Legacy four-byte-key sentinel corruption refused");

            changed = index.RawData.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(149), 8);
            File.WriteAllBytes(temporaryFile, changed);
            reject(() => NdxHfsqlIndexFile.Open(temporaryFile).ReadFlatIndex(0), "Legacy index width is not assumed to be eight");

            File.WriteAllBytes(temporaryFile, memos.RawData.Span[..295].ToArray());
            reject(() => MmoHfsqlMemoFile.Open(temporaryFile).ReadMemo(210), "Truncated version 15 memo payload refused");

            changed = project.RawData.ToArray();
            changed[14] = 16;
            File.WriteAllBytes(temporaryFile, changed);
            reject(() => WdpWindevProject.Open(temporaryFile), "Version 16 is not inferred from version 15");

            changed = data.RawData.ToArray();
            changed[14] = 16;
            File.WriteAllBytes(temporaryFile, changed);
            reject(() => FicHfsqlDataFile.Open(temporaryFile), "Physical version 16 remains unvalidated");

            byte[] encodedTextEvent = new byte[37];

            encodedTextEvent[0] = 0x10;
            BinaryPrimitives.WriteUInt32LittleEndian(encodedTextEvent.AsSpan(1), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(encodedTextEvent.AsSpan(5), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(encodedTextEvent.AsSpan(22), 2);
            encodedTextEvent[26] = 0xc3;
            encodedTextEvent[27] = 0xa9;
            BinaryPrimitives.WriteUInt32LittleEndian(encodedTextEvent.AsSpan(28), 1);
            encodedTextEvent[32] = (byte)'A';
            File.WriteAllBytes(temporaryFile, ReplaceSource(project, encodedTextEvent));

            WdpWindevProject explicitEncoding = WdpWindevProject.Open(temporaryFile);

            check(explicitEncoding.CodeBlocks.Single().Source == "Ã©", "Legacy source uses explicit CP1252 even when bytes also form valid UTF8");

            foreach (int offset in new int[] { 38, 55, 59 })
            {
                byte[] source = project.Sections[2].DecodedBlocks.Single().Data.ToArray();

                BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(offset), uint.MaxValue);
                File.WriteAllBytes(temporaryFile, ReplaceSource(project, source));

                WdpWindevProject malformed = WdpWindevProject.Open(temporaryFile);

                check(malformed.CodeBlocks.Count == 5, "Oversized legacy line count, source length or author length cannot consume another event");
            }
        }
        finally
        {
            File.Delete(temporaryFile);
        }

        checks.Add("WINDEV 15: six supplied families; 19-section WDP with unresolved references; CP1252 identities and window title; 23 window/6 project source events with bounded raw length-prefixed lines and owner tag 0a; WDD analysis/table/field tags 04/12/10; four-byte automatic identifier code 38; 123-byte FIC slot at 1688; four-byte NDX key and sentinel; 46-byte CP1252 memo. Property string candidates remain heuristic. Versions 16-23 and unprovided families remain unvalidated.");
    }

    private static byte[] ReplaceSource(WdpWindevProject project, byte[] source)
    {
        byte[] encoded = EncodeLiteralLzw(source);
        byte[] section = new byte[15 + encoded.Length];

        project.Sections[2].RawData.Span[..4].CopyTo(section);
        section[4] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(5), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(9), (uint)source.Length);
        encoded.CopyTo(section, 15);

        byte[] header = project.RawData.Span[..project.Header.PayloadOffset].ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(86 + 2 * 5), (uint)section.Length);

        using MemoryStream output = new MemoryStream();

        output.Write(header);

        foreach (WindevSection original in project.Sections)
        {
            output.Write(original.Index == 2 ? section : original.RawData.Span);
        }

        return output.ToArray();
    }

    private static byte[] EncodeLiteralLzw(byte[] source)
    {
        byte[] encoded = new byte[((source.Length * 2 + 1) * 9 + 7) / 8];
        int bitOffset = 0;

        foreach (byte value in source)
        {
            WriteCode(encoded, ref bitOffset, 0);
            WriteCode(encoded, ref bitOffset, value + 1);
        }

        WriteCode(encoded, ref bitOffset, 257);

        return encoded;
    }

    private static void WriteCode(byte[] output, ref int bitOffset, int code)
    {
        for (int bitIndex = 0; bitIndex < 9; bitIndex++, bitOffset++)
        {
            output[bitOffset / 8] |= (byte)(((code >> bitIndex) & 1) << (bitOffset % 8));
        }
    }
}
