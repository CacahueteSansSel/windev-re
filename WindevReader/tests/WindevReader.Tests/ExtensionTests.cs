using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using WindevReader;

internal static class ExtensionTests
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject, List<string> checks)
    {
        string project = Path.Combine(root, "Origamide");
        int previews = 0, matchingDimensions = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*.wdw", SearchOption.AllDirectories))
        {
            var window = WdwWindevWindow.Open(file); var preview = window.Preview;
            check(preview is not null, "BMP preview recognized in every WDW");
            check(window.Bounds is not null, "Window geometry independently recognized");
            bool sameSize = preview!.Width == window.Bounds!.Width && preview.Height == window.Bounds.Height;
            if (sameSize) matchingDimensions++;
            if (Path.GetDirectoryName(file) == project) check(sameSize, "Application preview matches window geometry");
            check(preview.BmpByteLength == 66 + preview.Width * preview.Height * 4, "Complete BMP file size");
            check(preview.BmpData.Span.SequenceEqual(window.Sections[4].DecodedBlocks.Single().Data.Span), "BMP export preserves exact decoded bytes");
            previews++;
        }
        check(previews == 28, "All 28 previews including templates");
        check(matchingDimensions == 23, "Five template previews have their own scaled/cropped dimensions");
        var small = WdwWindevWindow.Open(Path.Combine(project, "FEN_Rapport1.wdw"));
        byte[] rgba = small.Preview!.GetRgba32();
        check(rgba.Length == 354 * 126 * 4 && rgba.AsSpan(0, 4).SequenceEqual(new byte[] { 100, 100, 100, 255 }), "Bottom-up BMP to top-down opaque RGBA");
        checks.Add("28 standard BMP previews validated, including dimensions, exact bytes and RGBA orientation/alpha handling.");

        string patch = Path.Combine(project, "Patch/Origamide.wdl.1.0.77.0.6");
        var library = WdlWindevLibrary.Open(patch);
        check(library.Name == "Origamide" && library.ApplicationVersion == "1.0.77.0", "Decoded WDL properties");
        check(library.Entries.Count == 138 && library.Entries.Count(e => e.IsResource) == 134, "WDL directory inventory");
        check(library.Header.PayloadOffset == 5374 && library.Header.PayloadByteLength == 502085, "WDL exact payload coverage");
        check(library.DecodedProperties.Length == 411 && library.DecodedDirectory.Length == 22800, "WDL decoded metadata lengths");
        var compiledJournal = library.ReadWindowProperties(library.Entries.Single(e => e.LogicalName == "FEN_Journal"));
        check(compiledJournal.DecodedByteLength == 151540 && compiledJournal.Strings[0].Value == "FEN_Journal", "Compiled window property LZW and root name");
        check(BinaryPrimitives.ReadUInt32LittleEndian(compiledJournal.Data.Span[48..]) == 1177, "Compiled window independently agrees with source geometry");
        var compiledReport = library.ReadWindowProperties(library.Entries.Single(e => e.LogicalName == "FEN_Rapport"));
        check(compiledReport.DecodedByteLength == 137149 && compiledReport.Strings[0].Value == "FEN_Rapport", "Second compiled window property buffer");
        check(WindevFile.Open(patch) is WdlWindevLibrary, "Numbered WDL patch dispatch by signature");
        var archive = library.Entries.Single(e => e.OriginalPath == "ARCHIVE.PNG");
        byte[] png = library.ReadResource(archive);
        byte[] originalPng = File.ReadAllBytes(Directory.EnumerateFiles(project).Single(f => Path.GetFileName(f).Equals("archive.png", StringComparison.OrdinalIgnoreCase)));
        check(SHA256.HashData(png).SequenceEqual(SHA256.HashData(originalPng)), "Archive PNG matches independent project resource");
        int pngCount = 0, iconCount = 0, svgCount = 0;
        foreach (var entry in library.Entries)
        {
            foreach (var chunk in entry.Chunks.Concat(entry.AuxiliaryChunks))
                check(library.ReadStoredChunk(entry, chunk).Length == chunk.StoredByteLength, "Stored WDL chunk boundaries");
            if (!entry.IsResource) continue;
            byte[] resource = library.ReadResource(entry);
            if (resource.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                ValidatePng(resource, check); pngCount++;
            }
            if (entry.IsLzwCompressed)
            {
                check(resource.Length == 370070 && resource.AsSpan(0, 6).SequenceEqual(new byte[] { 0, 0, 1, 0, 6, 0 }), "Compressed resource produces a six-image ICO");
                for (int i = 0; i < 6; i++)
                {
                    int at = 6 + i * 16;
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(resource.AsSpan(at + 8));
                    uint offset = BinaryPrimitives.ReadUInt32LittleEndian(resource.AsSpan(at + 12));
                    check((ulong)offset + length <= (ulong)resource.Length, "ICO image directory boundaries");
                }
                iconCount++;
            }
            if (resource.Length > 0 && resource[0] == (byte)'<')
            {
                using var stream = new MemoryStream(resource);
                using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                var xml = XDocument.Load(reader);
                check(xml.Root?.Name == XName.Get("svg", "http://www.w3.org/2000/svg"), "XML resources are well-formed SVG documents");
                svgCount++;
            }
        }
        check(pngCount == 128 && iconCount == 2, "All PNG CRCs and both compressed ICOs verified");
        check(svgCount == 4, "Four SVG resources validated, with and without XML declarations");
        checks.Add("WDL: 138 entries, 134 resources; all chunks bounded; 128 complete PNGs with valid CRCs, 2 compressed ICOs and independent PNG comparison.");

        var fic = FicHfsqlDataFile.Open(Path.Combine(project, "RADFic.FIC"));
        var ndx = NdxHfsqlIndexFile.Open(Path.Combine(project, "RADFic.ndx"));
        var memo = MmoHfsqlMemoFile.Open(Path.Combine(project, "RADRub.mmo"));
        check(ndx.DeclaredIndexCount == 4 && ndx.DirectoryOffset == 7975, "NDX terminal directory header");
        check(ndx.IndexDescriptorOffsets.SequenceEqual(new[] { 135, 1861, 3899, 5937 }), "NDX four independent descriptor pointers");
        check(ndx.ReadIndexRegion(0).Length == 1726, "NDX descriptor region boundaries");
        check(fic.HasSameIdentityAs(ndx) && !fic.HasSameIdentityAs(memo), "Supplied RADRub memo belongs to another table");
        check(memo.HasSameIdentityAs(FicHfsqlDataFile.Open(Path.Combine(project, "RADRub.FIC"))), "RADRub companions linked by GUID");
        check(memo.RawData.Length == 210 && !memo.HasBytesAfterHeader && memo.RawHeaderSlots.All(s => s == ulong.MaxValue), "RAD memo header contains sentinel slots only");
        var analysis = WddWindevAnalysis.Open(Path.Combine(project, "Origamide.wdd"));
        check(!fic.MatchesAnalysis(analysis) && fic.FindTableDefinition(analysis) is null, "RAD files do not match application WDD");
        check(!fic.CanReadRecords && fic.PasswordStatus == HfsqlPasswordStatus.Unknown, "Unknown protection and unavailable record decoding remain explicit");
        int physicalCount = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => new[] { ".fic", ".ndx", ".mmo" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            check(WindevFile.Open(file) is HfsqlPhysicalFile, "HFSQL signature/header dispatch"); physicalCount++;
        }
        check(physicalCount == 427, "All 427 physical HFSQL files inspected");
        checks.Add("427 physical HFSQL headers validated; companion GUID links and application-analysis mismatch verified. Record/index/memo decoding and passwords remain unsupported.");

        string temporary = Path.Combine(Path.GetTempPath(), "windev-extension-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string bad = Path.Combine(temporary, "bad.bin");
            foreach (int length in new[] { 0, 19, 31, 297, 309, 5369, 5373, library.RawData.Length - 1 })
            { File.WriteAllBytes(bad, library.RawData.Span[..length].ToArray()); reject(() => WdlWindevLibrary.Open(bad), "Truncated WDL rejection"); }
            byte[] changed = library.RawData.ToArray(); changed[100] ^= 0x80; File.WriteAllBytes(bad, changed);
            reject(() => WdlWindevLibrary.Open(bad), "Damaged WDL metadata rejection");
            changed = library.RawData.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(28), uint.MaxValue); File.WriteAllBytes(bad, changed);
            reject(() => WdlWindevLibrary.Open(bad), "Overflowing WDL properties length rejection");
            reject(() => WdlWindevLibrary.Open(patch, new() { MaxArchiveEntries = 2 }), "WDL entry budget rejection");
            reject(() => WdlWindevLibrary.Open(patch, new() { MaxDecodedBlockBytes = 64 }), "WDL decompression budget rejection");
            reject(() => WdlWindevLibrary.Open(patch, new() { MaxTotalDecodedBytes = 1000 }), "WDL cumulative metadata budget rejection before allocation");
            reject(() => library.ReadResource(library.Entries[0]), "Compiled window cannot be extracted as a raw resource");
            foreach (int length in new[] { 0, 19, 52, 85, 89 })
            { File.WriteAllBytes(bad, fic.RawData.Span[..length].ToArray()); reject(() => FicHfsqlDataFile.Open(bad), "Truncated HFSQL header rejection"); }
            changed = fic.RawData.ToArray(); changed[52] = 65; File.WriteAllBytes(bad, changed);
            reject(() => FicHfsqlDataFile.Open(bad), "Unterminated HFSQL GUID rejection");
            changed[52] = 0; changed[20] = (byte)'Z'; File.WriteAllBytes(bad, changed);
            reject(() => FicHfsqlDataFile.Open(bad), "Non-hexadecimal HFSQL GUID rejection");
            reject(() => NdxHfsqlIndexFile.Open(fic.FileName), "Wrong physical reader rejection");
            changed = ndx.RawData.ToArray(); BinaryPrimitives.WriteUInt64LittleEndian(changed.AsSpan(111), ulong.MaxValue); File.WriteAllBytes(bad, changed);
            reject(() => NdxHfsqlIndexFile.Open(bad), "NDX directory overflow rejection");
            changed = ndx.RawData.ToArray(); BinaryPrimitives.WriteUInt64LittleEndian(changed.AsSpan(ndx.DirectoryOffset), 12); File.WriteAllBytes(bad, changed);
            reject(() => NdxHfsqlIndexFile.Open(bad), "NDX pointer into common header rejected");
            byte[] bmp = small.Preview.BmpData.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(18), int.MaxValue);
            File.WriteAllBytes(bad, ReplacePreview(small, bmp));
            reject(() => WdwWindevWindow.Open(bad), "Malformed preview dimensions rejected without pixel allocation");
            bmp = small.Preview.BmpData.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 4);
            File.WriteAllBytes(bad, ReplacePreview(small, bmp));
            reject(() => WdwWindevWindow.Open(bad), "Preview pixel/header overlap rejected");
        }
        finally { Directory.Delete(temporary, true); }
        checks.Add("New-family negative tests: truncated/altered archives, oversized metadata, entry budgets, wrong physical types, invalid GUIDs and corrupt BMP boundaries.");
    }

    private static void ValidatePng(byte[] data, Action<bool, string> check)
    {
        int cursor = 8; bool ended = false;
        while (cursor < data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cursor)));
            check(length <= data.Length - cursor - 12, "PNG chunk boundary");
            uint crc = uint.MaxValue;
            foreach (byte value in data.AsSpan(cursor + 4, length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            check(~crc == BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cursor + length + 8)), "PNG CRC");
            ended = data.AsSpan(cursor + 4, 4).SequenceEqual("IEND"u8); cursor += length + 12;
            if (ended) break;
        }
        check(ended && cursor == data.Length, "PNG IEND and complete byte coverage");
    }

    private static byte[] ReplacePreview(WdwWindevWindow original, byte[] bmp)
    {
        // Independent literal-only encoder: clear before each group of 200 literal codes, always nine bits.
        var codes = new List<int>();
        for (int p = 0; p < bmp.Length; p++) { if (p % 200 == 0) codes.Add(0); codes.Add(bmp[p] + 1); }
        codes.Add(257); byte[] packed = new byte[(codes.Count * 9 + 7) / 8]; int position = 0;
        foreach (int code in codes)
            for (int bit = 0; bit < 9; bit++, position++) packed[position / 8] |= (byte)(((code >> bit) & 1) << (position % 8));
        var wrapper = new byte[packed.Length + 11]; wrapper[0] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(1), (uint)packed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(5), (uint)bmp.Length); packed.CopyTo(wrapper, 11);
        using var output = new MemoryStream(); output.Write(original.RawData.Span[..original.Header.PayloadOffset]);
        foreach (var section in original.Sections) output.Write(section.Index == 4 ? wrapper : section.RawData.Span);
        byte[] result = output.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(86 + 4 * 5), (uint)wrapper.Length);
        return result;
    }
}
