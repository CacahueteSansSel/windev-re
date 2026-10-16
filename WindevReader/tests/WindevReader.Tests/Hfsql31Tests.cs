using System.Buffers.Binary;
using WindevReader;

internal static class Hfsql31Tests
{
    internal static void Run(
        string directory,
        string originalRoot,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WddWindevAnalysis analysis = WddWindevAnalysis.Open(Path.Combine(originalRoot, "Origamide/Origamide.wdd"));
        FicHfsqlDataFile holiday = FicHfsqlDataFile.Open(Path.Combine(directory, "jour_ferie.fic"));
        FicHfsqlDataFile weeks = FicHfsqlDataFile.Open(Path.Combine(directory, "semaine_cours.fic"));
        NdxHfsqlIndexFile hi = NdxHfsqlIndexFile.Open(Path.Combine(directory, "jour_ferie.ndx"));
        NdxHfsqlIndexFile wi = NdxHfsqlIndexFile.Open(Path.Combine(directory, "semaine_cours.ndx"));

        check(holiday.HasSameIdentityAs(hi) && weeks.HasSameIdentityAs(wi), "31 companion GUIDs");

        check(holiday.MatchesAnalysis(analysis) && weeks.MatchesAnalysis(analysis), "New files match original WDD 31");

        WindevDataTable ht = holiday.FindTableDefinition(analysis)!;

        check(
            ht.RawRecordByteLength == 70 && ht.Fields.Single(field => field.Name == "MOISJOUR").IsCompositeKey,
            "Composite key metadata and actual slot length");

        check(
            ht.Fields.Single(field2 => field2.Name == "MOISJOUR").Type == "CompositeKey",
            "Refined composite type label");

        check(
            !analysis.Tables.SelectMany(table => table.Fields).Single(field3 => field3.Name == "HASH").IsCompositeKey,
            "Ordinary binary field with the same flags is not a composed key");

        HfsqlFixedRecordLayout hLayout = holiday.CreateFixedRecordLayout(analysis, 1763, 8);
        HfsqlFixedRecordLayout wLayout = weeks.CreateFixedRecordLayout(analysis, 1627, 1);

        check(
            hLayout.RecordByteLength == 70 && hLayout.Fields.Count == 4 && !hLayout.Fields.Any(field4 => field4.Name == "MOISJOUR"),
            "Composite key consumes no FIC bytes");

        check(
            hLayout.Fields.Select(field5 => field5.Offset).SequenceEqual(new[] { 9, 17, 18, 19 }),
            "31 holiday physical field offsets");

        check(
            wLayout.RecordByteLength == 35 && wLayout.Fields.Select(field6 => field6.Offset).SequenceEqual(new[] { 9, 17, 26 }),
            "31 week date offsets");

        HfsqlFixedRecordView rows = holiday.ReadFixedRecords(hLayout);
        HfsqlFixedRecordView weekRows = weeks.ReadFixedRecords(wLayout);
        byte[] days = [1, 1, 8, 14, 15, 1, 11, 25];
        byte[] months = [1, 5, 5, 7, 8, 11, 11, 12];

        for (int index = 0; index < 8; index++)
        {
            check((long)rows[index].Values["IDJOUR_FERIE"] == index + 1, "31 FIC IDs");

            check(
                (byte)rows[index].Values["JOUR"] == days[index] && (byte)rows[index].Values["MOIS"] == months[index],
                "Unsigned one-byte values");
        }

        check(
            (string)rows[3].Values["LIBELLE"] == "Fête nationale" && (string)rows[7].Values["LIBELLE"] == "Noël",
            "Clear CP1252 accents in 31 records");

        check(
            (string)weekRows[0].Values["DATEDEB"] == "20260504" && (string)weekRows[0].Values["DATEFIN"] == "20260508",
            "Date8Z values preserved as yyyyMMdd strings");

        check(
            hi.DirectoryOffset == 9971 && hi.IndexDescriptorOffsets.SequenceEqual(new[] { 135, 2893, 5221, 7549 }),
            "31 holiday terminal directory");

        check(
            wi.DirectoryOffset == 8529 && wi.IndexDescriptorOffsets.SequenceEqual(new[] { 135, 2893, 5711 }),
            "31 week terminal directory");

        for (int byteIndex = 0; byteIndex < 4; byteIndex++)
        {
            HfsqlFlatIndex page = hi.ReadFlatIndex(byteIndex);

            check(page.Entries.Count == 8 && page.DeclaredEntryCount == 9, "31 flat page includes one terminal sentinel");

            foreach (HfsqlIndexEntry entry in page.Entries)
            {
                check(
                    entry.RawReference1 == entry.RawReference2 && entry.RawReference1 is >= 1 and <= 8,
                    "Two raw references agree with both ID and physical ordinal in these witnesses");

                int index2 = (int)entry.RawReference1 - 1;
                bool agrees = byteIndex switch
                {
                    0 => (BinaryPrimitives.ReadUInt64BigEndian(entry.Key.Span) ^ 0x8000000000000000UL) == (ulong)(index2 + 1),
                    1 => entry.Key.Span[0] == days[index2],
                    2 => entry.Key.Span[0] == months[index2],
                    3 => entry.Key.Span.SequenceEqual(new[] { months[index2], days[index2] }),
                    _ => false
                };

                check(agrees, "Every flat key matches independent FIC values");
            }
        }

        for (int byteIndex2 = 0; byteIndex2 < 3; byteIndex2++)
        {
            HfsqlFlatIndex page = wi.ReadFlatIndex(byteIndex2);

            check(page.Entries.Count == 1 && page.DeclaredEntryCount == 2, "31 single-row page and sentinel");

            if (byteIndex2 > 0)
            {
                check(
                    System.Text.Encoding.ASCII.GetString(page.Entries[0].Key.Span) == (string)weekRows[0].Values[byteIndex2 == 1 ? "DATEDEB" : "DATEFIN"] + "\0",
                    "Date keys match FIC date strings with terminator");
            }
        }

        string temporaryBuffer = Path.Combine(Path.GetTempPath(), "hfsql31-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            byte[] bytes = hi.RawData.ToArray();

            bytes[hi.ReadFlatIndex(0).TerminalSentinelOffset] = 0;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => NdxHfsqlIndexFile.Open(temporaryBuffer).ReadFlatIndex(0), "Corrupt flat terminal sentinel");

            bytes = hi.RawData.ToArray();

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(199), uint.MaxValue);

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(204), uint.MaxValue);

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => NdxHfsqlIndexFile.Open(temporaryBuffer).ReadFlatIndex(0), "Unbounded flat count rejected");

            bytes = hi.RawData.ToArray();

            bytes[224] = 0xFF;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(() => NdxHfsqlIndexFile.Open(temporaryBuffer).ReadFlatIndex(0), "Out-of-order flat key rejected");

            bytes = weeks.RawData.ToArray();

            bytes[1644] = (byte)'X';

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(
                () => FicHfsqlDataFile.Open(temporaryBuffer).ReadFixedRecords(wLayout)[0].ToString(),
                "Non-digit Date8Z rejected");

            bytes = weeks.RawData.ToArray();

            bytes[1652] = 1;

            File.WriteAllBytes(temporaryBuffer, bytes);

            reject(
                () => FicHfsqlDataFile.Open(temporaryBuffer).ReadFixedRecords(wLayout)[0].ToString(),
                "Non-terminated Date8Z rejected");
        }
        finally
        {
            File.Delete(temporaryBuffer);
        }

        reject(
            () => NdxHfsqlIndexFile.Open(hi.FileName, new WindevOpenOptions { MaxIndexEntries = 2 }).ReadFlatIndex(0),
            "Flat key allocation budget");

        NdxHfsqlIndexFile rad = NdxHfsqlIndexFile.Open(Path.Combine(originalRoot, "Origamide/RADFic.ndx"));

        reject(() => rad.ReadFlatIndex(0), "Opaque RAD index is not accepted as a clear flat page");

        checks.Add("HFSQL 31: both FICs matched WDD; eight holiday slots plus one week slot; compound key omitted from FIC; 7 flat NDX pages, 35 real keys and 7 sentinels; every key cross-checked against FIC; date strings, CP1252, corruption and budgets verified.");
    }
}
