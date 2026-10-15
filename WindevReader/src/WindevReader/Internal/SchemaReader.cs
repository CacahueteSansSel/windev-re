namespace WindevReader.Internal;
internal sealed record DecodedSchema(string Name, string Guid, uint Generation,
    IReadOnlyList<WindevDataTable> Tables, IReadOnlyList<WindevSchemaDirectoryEntry> Directory,
    uint RelationCount, IReadOnlyList<WindevDataRelation> Relations, ReadOnlyMemory<byte> Data);
internal static class SchemaReader
{
    private static byte ReadCompositeMarker(ReadOnlySpan<byte> data, int suffix)
    { Binary.Require(data, suffix, 32); return data[suffix + 31]; }

    public static DecodedSchema Read(ReadOnlyMemory<byte> memory, WindevOpenOptions options, ushort version)
    {
        ReadOnlySpan<byte> data = memory.Span; Binary.Require(data, 0, 252);
        if (Binary.U16(data, 0) != 0x60 || Binary.U16(data, 2) != 0x061d)
            throw new UnsupportedWindevFormatException("Unsupported decoded WDD revision");
        uint sum = 0; int j;
        for (j = 8; j <= data.Length - 4; j += 4) sum = unchecked(sum + Binary.U32(data, j));
        for (; j < data.Length; j++) sum = unchecked(sum + data[j]);
        if (sum != Binary.U32(data, 4)) throw new WindevFormatException("WDD checksum mismatch", 4);
        var directory = new List<WindevSchemaDirectoryEntry>();
        string[] labels = ["Analysis", "Tables", "Relations", "Directory3", "TableNameIndex", "Directory5", "Directory6", "Directory7"];
        for (int k = 0; k < 8; k++)
        {
            int at = 8 + k * 12; ulong pointer = Binary.U64(data, at); uint count = Binary.U32(data, at + 8);
            Binary.Offset(pointer, data.Length); directory.Add(new(labels[k], (long)pointer, count));
        }
        int root = Binary.Offset(Binary.U64(data, 8), data.Length);
        if (data[root++] != 5) throw new UnsupportedWindevFormatException("Unsupported WDD analysis record tag", root - 1);
        string name = BinaryText.CString(data, ref root, options.MaxStringBytes);
        _ = BinaryText.CString(data, ref root, options.MaxStringBytes); // Original directory.
        _ = BinaryText.CString(data, ref root, options.MaxStringBytes); // Auxiliary metadata, empty in this corpus; password semantics unverified.
        string guid = BinaryText.CString(data, ref root, options.MaxStringBytes);
        uint generation = Binary.U32(data, root);
        int tableCount = Binary.Count(Binary.U32(data, 64), 10000, 64);
        if (tableCount != Binary.U32(data, 28)) throw new WindevFormatException("WDD table directory count mismatch", 64);
        int index = Binary.Offset(Binary.U64(data, 56), data.Length);
        var tableLocations = new List<(string Name, int Offset)>();
        for (int k = 0; k < tableCount; k++)
        {
            string key = BinaryText.CString(data, ref index, options.MaxStringBytes);
            int offset = Binary.Offset(Binary.U64(data, index), data.Length); index += 8;
            tableLocations.Add((key, offset));
        }
        var tables = new List<WindevDataTable>();
        foreach (var entry in tableLocations)
        {
            int at = entry.Offset; Binary.Require(data, at, 9);
            if (data[at] != (version == 28 ? 0x20 : 0x22)) throw new UnsupportedWindevFormatException("Unsupported WDD table record tag", at);
            uint id = Binary.U32(data, at + 1); int p = at + 9;
            var strings = new string[6];
            for (int k = 0; k < strings.Length; k++) strings[k] = BinaryText.CString(data, ref p, options.MaxStringBytes);
            if (strings[0] != entry.Name) throw new WindevFormatException("WDD indexed table name mismatch", at);
            uint recordBytes = Binary.U32(data, p + 12);
            p += 48; string tableGuid = BinaryText.CString(data, ref p, options.MaxStringBytes);
            _ = BinaryText.CString(data, ref p, options.MaxStringBytes); p += 2;
            string duplicatedName = BinaryText.CString(data, ref p, options.MaxStringBytes); p += 2;
            if (duplicatedName != entry.Name) throw new WindevFormatException("WDD table record name mismatch", p);
            int count = Binary.Count(Binary.U32(data, p + 8), 10000, p + 8);
            int fieldIndex = Binary.Offset(Binary.U64(data, p + 70), data.Length);
            if (Binary.U32(data, p + 78) != count) throw new WindevFormatException("WDD field directory count mismatch", p + 78);
            Binary.Require(data, fieldIndex, 4 + count * 12);
            var fields = new List<WindevDataField>();
            for (int k = 0; k < count; k++)
            {
                int fieldAt = Binary.Offset(Binary.U64(data, fieldIndex + 4 + k * 12), data.Length);
                Binary.Require(data, fieldAt, 17);
                if (data[fieldAt] != (version == 28 ? 0x16 : 0x20)) throw new UnsupportedWindevFormatException("Unsupported WDD field record tag", fieldAt);
                if (Binary.U32(data, fieldAt + 9) != id) throw new WindevFormatException("WDD field belongs to another table", fieldAt);
                int q = fieldAt + 17;
                string fieldName = BinaryText.CString(data, ref q, options.MaxStringBytes);
                string caption = BinaryText.CString(data, ref q, options.MaxStringBytes);
                string fieldGuid = BinaryText.CString(data, ref q, options.MaxStringBytes);
                string mask = BinaryText.CString(data, ref q, options.MaxStringBytes);
                fields.Add(new(Binary.U32(data, fieldAt + 1), fieldName, caption, fieldGuid, mask,
                    Binary.U16(data, q), Binary.U32(data, q + 2), Binary.U16(data, q + 7), Binary.U16(data, q + 9), fieldAt) { RawCompositeMarker = ReadCompositeMarker(data, q) });
            }
            tables.Add(new(id, strings[0], strings[1], strings[2], strings[3], strings[5], tableGuid, at, fields.AsReadOnly()) { RawRecordByteLength = recordBytes });
        }
        var relations = new List<WindevDataRelation>();
        uint relationCount = Binary.U32(data, 40);
        if (version == 28 && relationCount != 0)
        {
            int count = Binary.Count(relationCount, 10000, 40);
            int p = Binary.Offset(Binary.U64(data, 32), data.Length);
            for (int k = 0; k < count; k++)
            {
                int start = p;
                Binary.Require(data, p, 9);
                if (data[p] != 1) throw new UnsupportedWindevFormatException("Unsupported WDD 28 relation tag", p);
                ulong id = Binary.U64(data, p + 1); p += 9;
                var names = new string[4]; var ids = new ulong[4];
                for (int i = 0; i < 4; i++)
                {
                    names[i] = BinaryText.CString(data, ref p, options.MaxStringBytes);
                    ids[i] = Binary.U64(data, p); p += 8;
                }
                var flags = new ushort[4];
                for (int i = 0; i < 4; i++) flags[i] = Binary.U16(data, p + 2 * i);
                p += 8;
                string relationName = BinaryText.CString(data, ref p, options.MaxStringBytes);
                uint trailer = Binary.U32(data, p); p += 4;
                var source = tables.FirstOrDefault(t => t.Id == ids[0] && t.Name == names[0]);
                var target = tables.FirstOrDefault(t => t.Id == ids[1] && t.Name == names[1]);
                if (source is null || target is null ||
                    !source.Fields.Any(f => f.Id == ids[2] && f.Name == names[2]) ||
                    !target.Fields.Any(f => f.Id == ids[3] && f.Name == names[3]))
                    throw new WindevFormatException("WDD relation endpoints do not match table/field identities", start);
                relations.Add(new(id, names[0], ids[0], names[1], ids[1], names[2], ids[2], names[3], ids[3],
                    relationName, Array.AsReadOnly(flags), trailer, start));
            }
        }
        return new(name, guid, generation, tables.AsReadOnly(), directory.AsReadOnly(), relationCount, relations.AsReadOnly(), memory);
    }
}
