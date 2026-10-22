namespace WindevReader.Internal;
internal sealed record DecodedSchema(
    string Name,
    string Guid,
    uint Generation,
    IReadOnlyList<WindevDataTable> Tables,
    IReadOnlyList<WindevSchemaDirectoryEntry> Directory,
    uint RelationCount,
    IReadOnlyList<WindevDataRelation> Relations,
    ReadOnlyMemory<byte> Data,
    IReadOnlyList<WindevDiagnostic> Diagnostics);
internal static class SchemaReader
{
    private const int headerLength = 252;

    private const int directoryEntryLength = 12;

    private const int maximumDefinitionCount = 10_000;

    public static DecodedSchema Read(ReadOnlyMemory<byte> memory, WindevOpenOptions options, ushort version)
    {
        ReadOnlySpan<byte> data = memory.Span;

        ValidateHeader(data, version);

        ValidateChecksum(data);

        IReadOnlyList<WindevSchemaDirectoryEntry> directory = ReadDirectory(data);
        int analysisOffset = Binary.Offset(Binary.U64(data, 8), data.Length);
        int cursor = analysisOffset;

        if (data[cursor++] != (version == 15 ? 4 : 5))
        {
            throw new UnsupportedWindevFormatException("Unsupported WDD analysis record tag", analysisOffset);
        }

        string name = BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        // Original directory and auxiliary text have no validated password semantics.
        BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        string guid = BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);
        uint generation = Binary.U32(data, cursor);
        IReadOnlyList<WindevDataTable> tables = ReadTables(data, options, version);
        uint relationCount = Binary.U32(data, 40);
        IReadOnlyList<WindevDataRelation> relations = version == 28 && relationCount != 0 ? ReadRelations(data, options, tables, relationCount) : Array.Empty<WindevDataRelation>();

        List<WindevDiagnostic> diagnostics = new List<WindevDiagnostic>();

        foreach (WindevDataTable table in tables)
        {
            if (table.RawRecordName != table.Name)
            {
                diagnostics.Add(new WindevDiagnostic(
                    "TableRecordNameMismatch",
                    DiagnosticSeverity.Warning,
                    $"A secondary table-record name differs from the indexed logical name at decoded schema offset 0x{table.RawRecordNameOffset:X}. The table index, field directories and field ownership were validated; the secondary name remains uninterpreted.",
                    1));
            }
        }

        return new DecodedSchema(name, guid, generation, tables, directory, relationCount, relations, memory, diagnostics.AsReadOnly());
    }

    private static void ValidateHeader(ReadOnlySpan<byte> data, ushort version)
    {
        Binary.Require(data, 0, version == 15 ? 192 : headerLength);

        if (Binary.U16(data, 0) != 0x60 || Binary.U16(data, 2) != 0x061d)
        {
            throw new UnsupportedWindevFormatException("Unsupported decoded WDD revision");
        }
    }

    private static void ValidateChecksum(ReadOnlySpan<byte> data)
    {
        uint checksum = 0;
        int cursor = 8;

        for (; cursor <= data.Length - 4; cursor += 4)
        {
            checksum = unchecked(checksum + Binary.U32(data, cursor));
        }

        for (; cursor < data.Length; cursor++)
        {
            checksum = unchecked(checksum + data[cursor]);
        }

        if (checksum != Binary.U32(data, 4))
        {
            throw new WindevFormatException("WDD checksum mismatch", 4);
        }
    }

    private static IReadOnlyList<WindevSchemaDirectoryEntry> ReadDirectory(ReadOnlySpan<byte> data)
    {
        string[] labels = ["Analysis", "Tables", "Relations", "Directory3", "TableNameIndex", "Directory5", "Directory6", "Directory7"];
        List<WindevSchemaDirectoryEntry> directory = new List<WindevSchemaDirectoryEntry>();

        for (int entryIndex = 0; entryIndex < labels.Length; entryIndex++)
        {
            int entryOffset = 8 + entryIndex * directoryEntryLength;
            ulong pointer = Binary.U64(data, entryOffset);
            uint count = Binary.U32(data, entryOffset + 8);

            Binary.Offset(pointer, data.Length);

            directory.Add(new WindevSchemaDirectoryEntry(labels[entryIndex], (long)pointer, count));
        }

        return directory.AsReadOnly();
    }

    private static IReadOnlyList<WindevDataTable> ReadTables(ReadOnlySpan<byte> data, WindevOpenOptions options, ushort version)
    {
        int tableCount = Binary.Count(Binary.U32(data, 64), maximumDefinitionCount, 64);

        if (tableCount != Binary.U32(data, 28))
        {
            throw new WindevFormatException("WDD table directory count mismatch", 64);
        }

        int indexOffset = Binary.Offset(Binary.U64(data, 56), data.Length);
        List<(string Name, int Offset)> locations = new List<(string Name, int Offset)>();

        for (int tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            string name = BinaryText.CString(data, ref indexOffset, options.MaxStringBytes, version == 15);
            int tableOffset = Binary.Offset(Binary.U64(data, indexOffset), data.Length);

            indexOffset += 8;

            locations.Add((name, tableOffset));
        }

        List<WindevDataTable> tables = new List<WindevDataTable>();
        int totalKeyComponents = 0;

        foreach ((string Name, int Offset) location in locations)
        {
            tables.Add(ReadTable(data, location.Name, location.Offset, options, version, ref totalKeyComponents));
        }

        return tables.AsReadOnly();
    }

    private static WindevDataTable ReadTable(
        ReadOnlySpan<byte> data,
        string indexedName,
        int tableOffset,
        WindevOpenOptions options,
        ushort version,
        ref int totalKeyComponents)
    {
        Binary.Require(data, tableOffset, 9);

        byte expectedTag = version switch
        {
            15 => 0x12,
            24 or 25 => 0x19,
            28 => 0x20,
            29 => 0x21,
            _ => 0x22
        };

        if (data[tableOffset] != expectedTag)
        {
            throw new UnsupportedWindevFormatException("Unsupported WDD table record tag", tableOffset);
        }

        uint tableId = Binary.U32(data, tableOffset + 1);
        int cursor = tableOffset + 9;
        string[] tableStrings = new string[6];

        for (int stringIndex = 0; stringIndex < tableStrings.Length; stringIndex++)
        {
            tableStrings[stringIndex] = BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);
        }

        if (tableStrings[0] != indexedName)
        {
            throw new WindevFormatException("WDD indexed table name mismatch", tableOffset);
        }

        uint recordByteLength = Binary.U32(data, cursor + 12);

        cursor += 48;

        string tableGuid = BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        cursor += 2;

        int recordNameOffset = cursor;
        string recordName = BinaryText.CString(data, ref cursor, options.MaxStringBytes, version == 15);

        cursor += 2;

        int fieldCount = Binary.Count(Binary.U32(data, cursor + 8), maximumDefinitionCount, cursor + 8);
        int fieldDirectoryOffset = Binary.Offset(Binary.U64(data, cursor + 70), data.Length);

        if (Binary.U32(data, cursor + 78) != fieldCount)
        {
            throw new WindevFormatException("WDD field directory count mismatch", cursor + 78);
        }

        Binary.Require(data, fieldDirectoryOffset, 4 + fieldCount * directoryEntryLength);

        List<WindevDataField> fields = new List<WindevDataField>();

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            int entryOffset = fieldDirectoryOffset + 4 + fieldIndex * directoryEntryLength;
            int fieldOffset = Binary.Offset(Binary.U64(data, entryOffset), data.Length);

            fields.Add(ReadField(data, fieldOffset, tableId, options, version));
        }

        IReadOnlyList<WindevDataField> resolvedFields = version is 28 or 31
            ? SchemaKeyReader.ResolveComponents(data, fields, options, ref totalKeyComponents)
            : fields.AsReadOnly();

        return new WindevDataTable(
            tableId,
            tableStrings[0],
            tableStrings[1],
            tableStrings[2],
            tableStrings[3],
            tableStrings[5],
            tableGuid,
            tableOffset,
            resolvedFields)
        {
            RawRecordByteLength = recordByteLength,
            RawRecordName = recordName,
            RawRecordNameOffset = recordNameOffset
        };
    }

    private static WindevDataField ReadField(ReadOnlySpan<byte> data, int fieldOffset, uint tableId, WindevOpenOptions options, ushort version)
    {
        Binary.Require(data, fieldOffset, 17);

        byte expectedTag = version switch
        {
            15 => 0x10,
            24 or 25 => 0x15,
            28 => 0x16,
            29 => 0x17,
            _ => 0x20
        };

        if (data[fieldOffset] != expectedTag)
        {
            throw new UnsupportedWindevFormatException("Unsupported WDD field record tag", fieldOffset);
        }

        if (Binary.U32(data, fieldOffset + 9) != tableId)
        {
            throw new WindevFormatException("WDD field belongs to another table", fieldOffset);
        }

        int suffixOffset = fieldOffset + 17;
        string name = BinaryText.CString(data, ref suffixOffset, options.MaxStringBytes, version == 15);
        string caption = BinaryText.CString(data, ref suffixOffset, options.MaxStringBytes, version == 15);
        string guid = BinaryText.CString(data, ref suffixOffset, options.MaxStringBytes, version == 15);
        string inputMask = BinaryText.CString(data, ref suffixOffset, options.MaxStringBytes, version == 15);

        Binary.Require(data, suffixOffset, 32);

        return new WindevDataField(
            Binary.U32(data, fieldOffset + 1),
            name,
            caption,
            guid,
            inputMask,
            Binary.U16(data, suffixOffset),
            Binary.U32(data, suffixOffset + 2),
            Binary.U16(data, suffixOffset + 7),
            Binary.U16(data, suffixOffset + 9),
            fieldOffset)
        {
            RawCompositeMarker = data[suffixOffset + 31],
            Key = version is 28 or 31
                ? new WindevFieldKey(suffixOffset + 27, Binary.U16(data, suffixOffset + 27), null, false, Array.Empty<WindevKeyComponent>())
                : null
        };
    }

    private static IReadOnlyList<WindevDataRelation> ReadRelations(
        ReadOnlySpan<byte> data,
        WindevOpenOptions options,
        IReadOnlyList<WindevDataTable> tables,
        uint relationCount)
    {
        int count = Binary.Count(relationCount, maximumDefinitionCount, 40);
        int cursor = Binary.Offset(Binary.U64(data, 32), data.Length);
        List<WindevDataRelation> relations = new List<WindevDataRelation>();

        for (int relationIndex = 0; relationIndex < count; relationIndex++)
        {
            int relationOffset = cursor;

            Binary.Require(data, cursor, 9);

            if (data[cursor] != 1)
            {
                throw new UnsupportedWindevFormatException("Unsupported WDD 28 relation tag", cursor);
            }

            ulong relationId = Binary.U64(data, cursor + 1);

            cursor += 9;

            string[] names = new string[4];
            ulong[] identifiers = new ulong[4];

            for (int endpointIndex = 0; endpointIndex < names.Length; endpointIndex++)
            {
                names[endpointIndex] = BinaryText.CString(data, ref cursor, options.MaxStringBytes);

                identifiers[endpointIndex] = Binary.U64(data, cursor);

                cursor += 8;
            }

            ushort[] flags = new ushort[4];

            for (int flagIndex = 0; flagIndex < flags.Length; flagIndex++)
            {
                flags[flagIndex] = Binary.U16(data, cursor + flagIndex * 2);
            }

            cursor += 8;

            string relationName = BinaryText.CString(data, ref cursor, options.MaxStringBytes);
            uint trailer = Binary.U32(data, cursor);

            cursor += 4;

            WindevDataTable? sourceTable = tables.FirstOrDefault(table => table.Name == names[0] && table.Id == identifiers[0]);
            WindevDataTable? targetTable = tables.FirstOrDefault(table => table.Name == names[1] && table.Id == identifiers[1]);

            if (sourceTable is null || targetTable is null || !sourceTable.Fields.Any(field => field.Name == names[2] && field.Id == identifiers[2]) || !targetTable.Fields.Any(field => field.Name == names[3] && field.Id == identifiers[3]))
            {
                throw new WindevFormatException("WDD relation endpoints do not match table/field identities", relationOffset);
            }

            relations.Add(new WindevDataRelation(
                relationId,
                names[0],
                identifiers[0],
                names[1],
                identifiers[1],
                names[2],
                identifiers[2],
                names[3],
                identifiers[3],
                relationName,
                Array.AsReadOnly(flags),
                trailer,
                relationOffset));
        }

        return relations.AsReadOnly();
    }
}
