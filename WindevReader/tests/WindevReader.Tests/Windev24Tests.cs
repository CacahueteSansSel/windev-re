using System.Buffers.Binary;
using WindevReader;
using WindevReader.Internal;

internal static class Windev24Tests
{
    internal static void RunSynthetic()
    {
        VerifySyntheticLengthDiagnostics();
        VerifySyntheticStructureReport();
        VerifySyntheticSnapshotRecovery();

        foreach (uint type in new uint[] { 3, 0x80002 })
        {
            byte[] data = CreateSyntheticContainer(type);
            PcsContainer parsed = PcsContainer.Parse(data, new WindevOpenOptions());

            RequireSynthetic(!parsed.Diagnostics.Any(item => item.Code == "UntestedVersion"));
            RequireSynthetic(parsed.Sections[1].DecodedBlocks.Count == 1);

            string file = Path.Combine(Path.GetTempPath(), "synthetic-v24-" + Guid.NewGuid().ToString("N") + (type == 3 ? ".wde" : ".wda"));

            try
            {
                File.WriteAllBytes(file, data);
                WindevDocument document = WindevDocument.Open(file);

                RequireSynthetic(type == 3 ? document is WdeWindevReport : document is WdaWindevAnalysisSource);
                RequireSynthetic(document.CodeBlocks.Count == 1 && document.CodeBlocks[0].Source == "RESULT 1");
                RequireSynthetic(document.CodeBlocks[0].Lines[0].Author == "Synthetic");
                RequireSynthetic(WindevFile.Open(file).GetType() == document.GetType());
                byte[] extended = new byte[data.Length + 32];
                data.CopyTo(extended, 0);
                extended.AsSpan(data.Length).Fill(0xA5);
                File.WriteAllBytes(file, extended);
                WindevDocument partial = WindevDocument.Open(file, new WindevOpenOptions { AllowTrailingPcsData = true });

                RequireSynthetic(partial.TrailingDataOffset == data.Length && partial.TrailingDataByteLength == 32);
                RequireSynthetic(partial.UninterpretedTrailingData.Span.SequenceEqual(extended.AsSpan(data.Length)));
                RequireSynthetic(partial.RawData.Span.SequenceEqual(extended));
                RequireSynthetic(partial.CodeBlocks[0].Source == document.CodeBlocks[0].Source);
            }
            finally
            {
                File.Delete(file);
            }

            byte[] malformed = (byte[])data.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(86), uint.MaxValue);
            RejectSynthetic(() => PcsContainer.Parse(malformed, new WindevOpenOptions()));
            RejectSynthetic(() => PcsContainer.Parse(data, new WindevOpenOptions { MaxDecodedBlockBytes = 1 }));

            foreach (ushort version in new ushort[] { 25, 29, 99 })
            {
                byte[] unsupported = (byte[])data.Clone();
                BinaryPrimitives.WriteUInt16LittleEndian(unsupported.AsSpan(14), version);
                RejectSynthetic(() => PcsContainer.Parse(unsupported, new WindevOpenOptions()));
            }
        }

        byte[] unverifiedFamily = CreateSyntheticContainer(7);
        RejectSynthetic(() => PcsContainer.Parse(unverifiedFamily, new WindevOpenOptions()));
        Console.WriteLine("Synthetic WDE/WDA 24 tests passed without private specimens.");
    }

    private static byte[] CreateSyntheticContainer(uint type)
    {
        byte[] source = SyntheticString("RESULT 1");
        byte[] author = SyntheticString("Synthetic");
        byte[] code = new byte[21 + 1 + source.Length + author.Length + 12];
        code[0] = 0x14;
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1), (uint)code.Length - 5);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(5), 1);
        code[21] = 3;
        source.CopyTo(code, 22);
        author.CopyTo(code, 22 + source.Length);

        byte[] compressedCode = SyntheticCompressed(code);
        byte[] codeSection = new byte[4 + compressedCode.Length];
        compressedCode.CopyTo(codeSection, 4);
        byte[][] sections = new byte[][]
        {
            SyntheticString("SyntheticDocument"),
            SyntheticCompressed(SyntheticString("SyntheticProperties")),
            codeSection
        };
        byte[] data = new byte[100 + sections.Sum(section => section.Length)];
        "PCS\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), type);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 68);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(81), 3);
        int cursor = 100;

        for (int index = 0; index < sections.Length; index++)
        {
            data[85 + index * 5] = (byte)index;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(86 + index * 5), (uint)sections[index].Length);
            sections[index].CopyTo(data, cursor);
            cursor += sections[index].Length;
        }

        return data;
    }

    private static void VerifySyntheticStructureReport()
    {
        byte[] original = CreateSyntheticContainer(3);
        byte[] extraBlock = SyntheticCompressed(SyntheticString("PrivateSyntheticContent"));
        byte[] extended = new byte[original.Length + extraBlock.Length];
        original.CopyTo(extended, 0);
        extraBlock.CopyTo(extended, original.Length);
        WindevPcsStructureReport report = WindevPcsStructure.Inspect(extended);

        RequireSynthetic(report.SectionCount == 3 && report.Sections.All(section => section.InBounds));
        RequireSynthetic(report.DeclaredEnd == original.Length && report.TrailingByteLength == extraBlock.Length);
        RequireSynthetic(report.ValidatedLzwBlocks.Any(block => block.Offset == original.Length &&
            block.AfterDeclaredSections && block.SectionIndex == null));
        RequireSynthetic(report.ValidatedLzwBlocks.Any(block => block.SectionIndex == 2));
        string serialized = System.Text.Json.JsonSerializer.Serialize(report);
        RequireSynthetic(!serialized.Contains("PrivateSyntheticContent") && !serialized.Contains("Synthetic") && !serialized.Contains("RESULT"));

        byte[] joined = new byte[original.Length * 2];
        original.CopyTo(joined, 0);
        original.CopyTo(joined, original.Length);
        WindevPcsStructureReport nested = WindevPcsStructure.Inspect(joined);
        RequireSynthetic(nested.EmbeddedContainers.Single().Offset == original.Length);
        RequireSynthetic(nested.EmbeddedContainers.Single().SectionCount == 3);
        RequireSynthetic(WindevPcsStructure.Inspect(extended, new WindevOpenOptions { MaxDecodedBlocks = 1 }).LzwScanLimited);
        RejectSynthetic(() => WindevPcsStructure.Inspect(extended, new WindevOpenOptions { MaxFileBytes = 85 }));
        RejectSynthetic(() => WindevPcsStructure.Inspect(new byte[85]));

        byte[] suffix = CreateSyntheticWindowSuffix();
        byte[] duplicate = new byte[original.Length + suffix.Length];
        original.CopyTo(duplicate, 0);
        suffix.CopyTo(duplicate, original.Length);
        WindevPcsStructureReport inferred = WindevPcsStructure.Inspect(duplicate);
        RequireSynthetic(inferred.EmbeddedContainers.Count == 0);
        RequireSynthetic(inferred.WindowDirectoryCandidates.Single().DirectoryOffset == original.Length + 85);
        RequireSynthetic(inferred.WindowDirectoryCandidates.Single().DeclaredEnd == duplicate.Length);
        RequireSynthetic(inferred.WindowDirectoryCandidates.Single().InferredPayloadOffset == original.Length + 145);
        duplicate[original.Length + 85 + 4 * 5] = 0;
        RequireSynthetic(WindevPcsStructure.Inspect(duplicate).WindowDirectoryCandidates.Count == 0);
    }

    private static byte[] CreateSyntheticWindowSuffix(bool recoverable = false)
    {
        byte[] compressed = SyntheticCompressed(SyntheticString("Synthetic"));
        byte[][] sections = Enumerable.Range(0, 12).Select(index => Array.Empty<byte>()).ToArray();
        sections[0] = SyntheticString(recoverable ? "SyntheticDocument" : "Synthetic");
        sections[1] = new byte[28 + compressed.Length];
        compressed.CopyTo(sections[1], 28);
        sections[2] = new byte[4 + compressed.Length];
        compressed.CopyTo(sections[2], 4);
        if (recoverable)
        {
            byte[] sourceFrame = SyntheticString("RESULT 2");
            byte[] author = SyntheticString("Synthetic");
            byte[] code = new byte[22 + sourceFrame.Length + author.Length + 12];
            code[0] = 0x15;
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1), (uint)code.Length - 5);
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(5), 1);
            code[21] = 3;
            sourceFrame.CopyTo(code, 22);
            author.CopyTo(code, 22 + sourceFrame.Length);
            byte[] encoded = SyntheticCompressed(code);
            sections[2] = new byte[4 + encoded.Length];
            encoded.CopyTo(sections[2], 4);
        }
        sections[4] = compressed;
        byte[] data = new byte[145 + sections.Sum(section => section.Length)];
        byte[] tags = new byte[] { 0, 17, 2, 0, 8, 19, 0, 5, 25, 10, 0, 6 };
        int cursor = 145;

        // Deliberately omit the PCS signature: only the independent directory anchors identify this candidate.
        for (int index = 0; index < sections.Length; index++)
        {
            data[85 + index * 5] = tags[index];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(86 + index * 5), (uint)sections[index].Length);
            sections[index].CopyTo(data, cursor);
            cursor += sections[index].Length;
        }

        return data;
    }

    private static void VerifySyntheticSnapshotRecovery()
    {
        byte[] original = CreateSyntheticContainer(2);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(14), 29);
        byte[] suffix = CreateSyntheticWindowSuffix(true);
        byte[] combined = new byte[original.Length + suffix.Length];
        original.CopyTo(combined, 0);
        suffix.CopyTo(combined, original.Length);
        WindevOpenOptions recovery = new WindevOpenOptions();
        RequireSynthetic(recovery.RecoverAppendedWindowData);
        RejectSynthetic(() => PcsContainer.Parse(combined, new WindevOpenOptions { RecoverAppendedWindowData = false }));
        PcsContainer result = PcsContainer.Parse(combined, recovery);
        RequireSynthetic(result.Sections.Count == 12 && result.Sections[^1].Offset + result.Sections[^1].ByteLength == combined.Length);
        RequireSynthetic(result.RecoveredDirectoryOffset == original.Length + 85);
        RequireSynthetic(result.RawData.AsSpan().SequenceEqual(combined));
        RequireSynthetic(result.Diagnostics.Single().Code == "RecoveredAppendedWindowSnapshot");

        string file = Path.Combine(Path.GetTempPath(), "synthetic-recovery-" + Guid.NewGuid().ToString("N") + ".wdw");

        try
        {
            File.WriteAllBytes(file, combined);
            WdwWindevWindow window = WdwWindevWindow.Open(file);
            RequireSynthetic(window.CodeBlocks.Single().Source == "RESULT 2");
            RequireSynthetic(((WdwWindevWindow)WindevFile.Open(file)).CodeBlocks.Single().Source == "RESULT 2");
            RequireSynthetic(window.RecoveredDirectoryOffset == result.RecoveredDirectoryOffset && window.TrailingDataByteLength == 0);
        }
        finally
        {
            File.Delete(file);
        }

        RejectSynthetic(() => PcsContainer.Parse(combined, new WindevOpenOptions { RecoverAppendedWindowData = true, MaxDecodedBlocks = 1 }));
        byte[] mismatched = (byte[])combined.Clone();
        int identityTextOffset = original.Length + 145 + 8;
        mismatched[identityTextOffset] ^= 1;
        RejectSynthetic(() => PcsContainer.Parse(mismatched, recovery));
        byte[] brokenAnchor = (byte[])combined.Clone();
        brokenAnchor[original.Length + 85 + 4 * 5] = 0;
        RejectSynthetic(() => PcsContainer.Parse(brokenAnchor, recovery));
    }

    private static void VerifySyntheticLengthDiagnostics()
    {
        byte[] original = CreateSyntheticContainer(3);

        foreach (int difference in new int[] { -1, 1, 32 })
        {
            byte[] changed = new byte[original.Length + difference];
            original.AsSpan(0, Math.Min(original.Length, changed.Length)).CopyTo(changed);

            try
            {
                PcsContainer.Parse(changed, new WindevOpenOptions());
            }
            catch (WindevFormatException exception)
            {
                RequireSynthetic(exception.Offset == 0x55);
                RequireSynthetic(exception.Message.Contains("WINDEV 24, type 0x3, sections 3, payload 0x64"));
                RequireSynthetic(exception.Message.Contains($"expected end 0x{original.Length:X}"));
                RequireSynthetic(exception.Message.Contains($"file length 0x{changed.Length:X}, delta {difference} bytes"));
                RequireSynthetic(exception.Message.Contains(difference > 0
                    ? "trailing bytes after declared sections" : "declared sections exceed file length"));

                WindevOpenOptions permissive = new WindevOpenOptions { AllowTrailingPcsData = true };

                if (difference > 0)
                {
                    PcsContainer partial = PcsContainer.Parse(changed, permissive);
                    PcsContainer baseline = PcsContainer.Parse(original, new WindevOpenOptions());
                    RequireSynthetic(partial.Diagnostics.Single().Code == "UninterpretedTrailingData");
                    RequireSynthetic(partial.Sections.Count == baseline.Sections.Count);

                    for (int index = 0; index < baseline.Sections.Count; index++)
                    {
                        RequireSynthetic(partial.Sections[index].RawData.Span.SequenceEqual(baseline.Sections[index].RawData.Span));
                    }

                    RejectSynthetic(() => PcsContainer.Parse(changed, new WindevOpenOptions
                    {
                        AllowTrailingPcsData = true,
                        MaxDecodedBlockBytes = 1
                    }));
                }
                else
                {
                    RejectSynthetic(() => PcsContainer.Parse(changed, permissive));
                }

                continue;
            }

            throw new Exception("Section size mismatch was silently accepted.");
        }
    }

    private static byte[] SyntheticString(string value)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] frame = new byte[text.Length + 13];
        new byte[] { 0xFF, 0xCE, 0x88, 0xF1 }.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)frame.Length);
        text.CopyTo(frame, 8);
        new byte[] { 0xFE, 0xED, 0x98, 0xE1 }.CopyTo(frame, frame.Length - 4);

        return frame;
    }

    private static byte[] SyntheticCompressed(byte[] data)
    {
        // Clear before each literal keeps all synthetic LZW codes nine bits wide.
        List<int> codes = new List<int>();

        foreach (byte value in data)
        {
            codes.Add(0);
            codes.Add(value + 1);
        }

        codes.Add(257);
        int encodedLength = (codes.Count * 9 + 7) / 8;
        byte[] block = new byte[11 + encodedLength];
        block[0] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(1), (uint)encodedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(5), (uint)data.Length);

        for (int index = 0; index < codes.Count; index++)
        {
            for (int bit = 0; bit < 9; bit++)
            {
                int position = index * 9 + bit;
                block[11 + position / 8] |= (byte)(((codes[index] >> bit) & 1) << (position % 8));
            }
        }

        return block;
    }

    private static void RequireSynthetic(bool condition)
    {
        if (!condition)
        {
            throw new Exception("Synthetic version 24 assertion failed.");
        }
    }

    private static void RejectSynthetic(Action action)
    {
        try
        {
            action();
        }
        catch (WindevFormatException)
        {
            return;
        }

        throw new Exception("Malformed or unverified synthetic container was accepted.");
    }

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
