using System.Buffers.Binary;
using WindevReader;
using WindevReader.Internal;

internal static class WddKeyTests
{
    internal static void Run(
        string filename,
        string previousAnalysisDirectory,
        string hfsql31Directory,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WddWindevAnalysis analysis = WddWindevAnalysis.Open(filename);
        WindevDataTable table = analysis.Tables.Single(candidate => candidate.Name == "PERSONNE");
        (string Name, string Type, WindevKeyKind Kind)[] expected =
        [
            ("IDPERSONNE", "AutomaticIdentifier64", WindevKeyKind.Primary),
            ("NOM", "Text", WindevKeyKind.None),
            ("PRENOM", "Text", WindevKeyKind.None),
            ("AGE1", "Int32", WindevKeyKind.Duplicates),
            ("FLAGADMIN", "Boolean", WindevKeyKind.Duplicates),
            ("IDPERSONNEPASCLE", "UInt64", WindevKeyKind.None),
            ("NOMPRENOMAGE1", "CompositeKey", WindevKeyKind.Duplicates)
        ];

        check(analysis.Header.WindevVersion == 28 && analysis.DecodedSchema.Length == 7148, "Extended WDD 28 has a checksum-valid decoded schema");
        check(analysis.Tables.Count == 2 && table.Fields.Count == 7, "Updated PERSONNE field directory includes the unsigned value and composite key");

        foreach ((string Name, string Type, WindevKeyKind Kind) item in expected)
        {
            WindevDataField field = table.Fields.Single(candidate => candidate.Name == item.Name);

            check(field.Type == item.Type, "Field type agrees with the supplied analysis description");
            check(field.KeyKind == item.Kind, "Key kind is independent of the field type and existing raw flags");
            check(field.Key?.RawKind == (ushort)item.Kind, "Observed key code is preserved");
        }

        WindevDataField primary = table.Fields[0];
        WindevDataField unsigned = table.Fields[5];
        WindevDataField composite = table.Fields[6];

        check(primary.IsPrimaryKey == true && primary.AllowsDuplicates == false, "Primary key interpretation is explicit");
        check(unsigned.RawTypeCode == 27 && unsigned.StorageSize == 8 && unsigned.IsPrimaryKey == false, "Unsigned eight-byte field is not an automatic identifier or a key");
        check(table.Fields[3].AllowsDuplicates == true && table.Fields[4].AllowsDuplicates == true, "Integer and boolean duplicate keys share the key-kind code");
        check(table.Fields[1].AllowsDuplicates is null, "A non-key field has no index duplicate policy");
        check(composite.IsCompositeKey && composite.Key?.ComponentsDecoded == true && composite.Key.ComponentsOffset == 5674, "Composite vector is located through its absolute decoded pointer");
        check(composite.KeyComponents.Select(component => component.FieldName).SequenceEqual(new string[] { "NOM", "PRENOM", "AGE1" }), "Ordered component names agree with the supplied analysis");
        check(composite.KeyComponents.Select(component => component.FieldOrdinal).SequenceEqual(new ushort[] { 1, 2, 3 }), "Component references are zero-based field-directory ordinals");
        check(composite.KeyComponents.Select(component => component.FieldId).SequenceEqual(new uint[] { 2, 3, 4 }), "Ordinals are resolved to distinct field identifiers");
        check(composite.KeyComponents.Select(component => component.Offset).SequenceEqual(new int[] { 5676, 5679, 5682 }), "Composite entries occupy three bytes each");
        check(composite.Size == 104 && composite.StorageSize == 106 && table.RawRecordByteLength == 132, "Composite key sizes remain distinct from the declared FIC stride");

        WindevDataField unclassified = analysis.Tables.Single(candidate => candidate.Name == "PERSONNE_MESSAGE").Fields[0];

        check(unclassified.Key?.RawKind == 1 && unclassified.KeyKind is null && unclassified.IsPrimaryKey is null, "Code 1 remains unclassified without a supplied semantic oracle");

        VerifyMutations(analysis, check, reject);
        VerifyUnsignedFixture(analysis, previousAnalysisDirectory, check);

        WddWindevAnalysis analysis31 = WddWindevAnalysis.Open(Path.Combine(hfsql31Directory, "matching-analysis.wdd"));
        WindevDataField monthDay = analysis31.Tables.Single(candidate => candidate.Name == "JOUR_FERIE").Fields.Single(field => field.Name == "MOISJOUR");

        check(monthDay.KeyKind == WindevKeyKind.Duplicates && monthDay.Key?.ComponentsOffset == 9233, "Version 31 composite pointer and duplicate key kind corroborate the profile");
        check(monthDay.KeyComponents.Select(component => component.FieldName).SequenceEqual(new string[] { "MOIS", "JOUR" }), "WDD 31 component order agrees independently with the decoded NDX keys");
        check(monthDay.KeyComponents.Select(component => component.FieldId).SequenceEqual(new uint[] { 9, 8 }), "Version 31 components refer to local ordinals rather than numeric field IDs");

        checks.Add("WDD keys: supplied PERSONNE 28 has primary IDPERSONNE, non-key text/UInt64 fields, duplicate AGE1/FLAGADMIN and ordered NOM/PRENOM/AGE1 composite components. u16 key kind at Q+27, pointer at Q+46, u16 count and three-byte component entries; MOIS/JOUR order corroborated in WDD/NDX 31. Unknown key codes and component options remain raw. Invalid pointers, ordinals, self-references, counts and budgets refused. A synthetic 132-byte FIC slot validates UInt64 decoding and composite omission; no new physical FIC was supplied.");
    }

    private static void VerifyMutations(WddWindevAnalysis analysis, Action<bool, string> check, Action<Action, string> reject)
    {
        byte[] changed = analysis.DecodedSchema.ToArray();

        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(3903), ushort.MaxValue);

        WindevDataTable unknown = Parse(changed).Tables.Single(table => table.Name == "PERSONNE");

        check(unknown.Fields[0].KeyKind is null && unknown.Fields[0].Key?.RawKind == ushort.MaxValue, "Unknown key code remains raw without a default classification");

        changed = analysis.DecodedSchema.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(4172), 2);

        check(Parse(changed).Tables.Single(table => table.Name == "PERSONNE").Fields[1].KeyKind == WindevKeyKind.Duplicates, "Key classification follows bytes rather than field names");

        changed = analysis.DecodedSchema.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(5676), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(5682), 1);
        changed[5678] = 0x80;

        WindevDataField reordered = Parse(changed).Tables.Single(table => table.Name == "PERSONNE").Fields[6];

        check(reordered.KeyComponents.Select(component => component.FieldName).SequenceEqual(new string[] { "AGE1", "PRENOM", "NOM" }), "Component order comes from the binary vector, not the composite name or caption");
        check(reordered.KeyComponents[0].RawOptions == 0x80, "Unknown per-component options are preserved without an inferred sort direction");

        foreach (ulong pointer in new ulong[] { ulong.MaxValue, (ulong)analysis.DecodedSchema.Length - 1 })
        {
            byte[] malformed = analysis.DecodedSchema.ToArray();

            BinaryPrimitives.WriteUInt64LittleEndian(malformed.AsSpan(5519), pointer);
            reject(() => Parse(malformed), "Out-of-range or truncated component directory rejected");
        }

        foreach (ushort ordinal in new ushort[] { 7, 6, 256 })
        {
            byte[] malformed = analysis.DecodedSchema.ToArray();

            BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(5676), ordinal);
            reject(() => Parse(malformed), "Foreign ordinal, self-reference or truncated ordinal interpretation rejected");
        }

        foreach (ushort count in new ushort[] { 0, ushort.MaxValue })
        {
            byte[] malformed = analysis.DecodedSchema.ToArray();

            BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(5674), count);
            reject(() => Parse(malformed), "Empty or oversized composite vector rejected");
        }

        changed = analysis.DecodedSchema.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(4168), 2);
        reject(() => Parse(changed), "Ambiguous reordered field ordinals are rejected rather than resolved to a guessed name");

        reject(() => WddWindevAnalysis.Open(analysis.FileName, new WindevOpenOptions { MaxIndexEntries = 2 }), "Schema component budget is enforced before allocation");
    }

    private static DecodedSchema Parse(byte[] data)
    {
        uint checksum = 0;

        for (int offset = 8; offset < data.Length; offset += 4)
        {
            checksum = unchecked(checksum + BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), checksum);

        return SchemaReader.Read(data, new WindevOpenOptions(), 28);
    }

    private static void VerifyUnsignedFixture(WddWindevAnalysis analysis, string directory, Action<bool, string> check)
    {
        FicHfsqlDataFile previous = FicHfsqlDataFile.Open(Path.Combine(directory, "PERSONNE.fic"));
        byte[] data = new byte[90 + 132];

        previous.RawData.Span[..90].CopyTo(data);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(90 + 9), 1);
        "Alpha"u8.CopyTo(data.AsSpan(90 + 17));
        "Beta"u8.CopyTo(data.AsSpan(90 + 68));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(90 + 119), 42);
        data[90 + 123] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(90 + 124), ulong.MaxValue);

        string temporaryFile = Path.Combine(Path.GetTempPath(), "wdd-unsigned-" + Guid.NewGuid().ToString("N") + ".fic");

        try
        {
            File.WriteAllBytes(temporaryFile, data);

            FicHfsqlDataFile synthetic = FicHfsqlDataFile.Open(temporaryFile);
            HfsqlFixedRecordLayout layout = synthetic.CreateFixedRecordLayout(analysis, 90, 1);
            HfsqlFixedRecord record = synthetic.ReadFixedRecords(layout)[0];

            check(layout.RecordByteLength == 132 && layout.Fields.Count == 6, "Synthetic fixture layout omits the logical composite key");
            check(layout.Fields[^1].Type == HfsqlFixedFieldType.UInt64 && layout.Fields[^1].Offset == 124, "Unsigned schema code maps to UInt64 in the explicit-layout reader");
            check(record.Values["IDPERSONNEPASCLE"] is ulong value && value == ulong.MaxValue, "Unsigned values above Int64.MaxValue retain their full range");
            check(!record.Values.ContainsKey("NOMPRENOMAGE1"), "Composite index key is not fabricated as an extra inline FIC value");
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }
}
