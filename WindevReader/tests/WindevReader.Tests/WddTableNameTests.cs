using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WindevReader;
using WindevReader.Internal;

internal static class WddTableNameTests
{
    internal static void Run(
        WddWindevAnalysis analysis,
        Action<bool, string> check,
        Action<Action, string> reject,
        List<string> checks)
    {
        WindevDataTable original = analysis.Tables[0];
        int originalNameOffset = FindRecordNameOffset(analysis.DecodedSchema.Span, original.Offset);
        DecodedSchema unchanged = Parse(analysis.DecodedSchema.ToArray(), analysis.Header.WindevVersion);

        check(original.RawRecordName == original.Name, "Existing secondary table name is preserved");
        check(original.RawRecordNameOffset == originalNameOffset, "Secondary name offset identifies the first CString byte");
        check(unchanged.Diagnostics.Count == 0, "Matching secondary names do not produce a compatibility warning");

        foreach (string secondaryName in new string[] { "ALIAS", "", "SuperNoteExtra_Archive_Été_2026" })
        {
            byte[] changed = RelocateTable(analysis, secondaryName);
            DecodedSchema decoded = Parse(changed, analysis.Header.WindevVersion);
            WindevDataTable table = decoded.Tables[0];
            WindevDiagnostic diagnostic = decoded.Diagnostics.Single();

            check(table.Name == original.Name && table.PhysicalName == original.PhysicalName, "Logical and physical names are not replaced by the secondary name");
            check(table.RawRecordName == secondaryName, "Different, empty and multibyte secondary names remain raw");
            check(table.RawRecordNameOffset == analysis.DecodedSchema.Length + originalNameOffset - original.Offset, "Relocated name offset is measured in decoded schema bytes");
            check(JsonSerializer.Serialize(table.Fields) == JsonSerializer.Serialize(original.Fields), "Variable-length secondary names preserve the field directory and all field data");
            check(JsonSerializer.Serialize(decoded.Tables.Skip(1)) == JsonSerializer.Serialize(analysis.Tables.Skip(1)), "Other table definitions remain unchanged");
            check(diagnostic.Code == "TableRecordNameMismatch" && diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.SectionIndex == 1, "Validated secondary-name divergence is exposed as a schema warning");
            check(diagnostic.Message.Contains($"0x{table.RawRecordNameOffset:X}") && !diagnostic.Message.Contains(original.Name, StringComparison.Ordinal) && (secondaryName.Length == 0 || !diagnostic.Message.Contains(secondaryName, StringComparison.Ordinal)), "Compatibility warning includes the offset without schema names");
        }

        byte[] fixture = RelocateTable(analysis, "ALIAS");
        int tableOffset = analysis.DecodedSchema.Length;
        int nameOffset = tableOffset + originalNameOffset - original.Offset;
        int metadataOffset = nameOffset + Encoding.UTF8.GetByteCount("ALIAS") + 3;

        byte[] malformed = (byte[])fixture.Clone();

        malformed[tableOffset + 9] ^= 1;
        RejectWithMessage(malformed, analysis.Header.WindevVersion, "WDD indexed table name mismatch", check);

        malformed = (byte[])fixture.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(metadataOffset + 78), (uint)original.Fields.Count + 1);
        RejectWithMessage(malformed, analysis.Header.WindevVersion, "WDD field directory count mismatch", check);

        malformed = (byte[])fixture.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(malformed.AsSpan(metadataOffset + 70), ulong.MaxValue);
        reject(() => Parse(malformed, analysis.Header.WindevVersion), "Divergent secondary name does not permit an invalid field directory pointer");

        malformed = (byte[])fixture.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(malformed.AsSpan(metadataOffset + 70), (ulong)malformed.Length - 1);
        reject(() => Parse(malformed, analysis.Header.WindevVersion), "Divergent secondary name does not permit a truncated directory");

        malformed = (byte[])fixture.Clone();
        malformed[original.Fields[0].Offset] = 0xff;
        RejectWithMessage(malformed, analysis.Header.WindevVersion, "Unsupported WDD field record tag", check);

        malformed = (byte[])fixture.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(original.Fields[0].Offset + 9), original.Id + 1);
        RejectWithMessage(malformed, analysis.Header.WindevVersion, "WDD field belongs to another table", check);

        malformed = (byte[])fixture.Clone();
        malformed.AsSpan(nameOffset).Fill(0x41);
        reject(() => Parse(malformed, analysis.Header.WindevVersion), "An unterminated secondary CString is still rejected");

        malformed = (byte[])fixture.Clone();
        RepairChecksum(malformed);
        malformed[4] ^= 1;
        reject(() => SchemaReader.Read(malformed, new WindevOpenOptions(), analysis.Header.WindevVersion), "Secondary-name compatibility does not disable the checksum");

        checks.Add($"WDD {analysis.Header.WindevVersion}: secondary table names are preserved independently of indexed logical names. Synthetic relocated records with different, empty and multibyte names retain fields and report offsets without names; corrupt checksums, logical names, directories, tags and field ownership remain rejected. The reported confidential analysis was not supplied.");
    }

    private static int FindRecordNameOffset(ReadOnlySpan<byte> data, int tableOffset)
    {
        int cursor = tableOffset + 9;

        for (int index = 0; index < 6; index++)
        {
            BinaryText.CString(data, ref cursor, data.Length);
        }

        cursor += 48;

        BinaryText.CString(data, ref cursor, data.Length);
        BinaryText.CString(data, ref cursor, data.Length);

        return cursor + 2;
    }

    private static byte[] RelocateTable(WddWindevAnalysis analysis, string secondaryName)
    {
        ReadOnlySpan<byte> source = analysis.DecodedSchema.Span;
        WindevDataTable table = analysis.Tables[0];
        int nameOffset = FindRecordNameOffset(source, table.Offset);
        int metadataOffset = nameOffset;

        BinaryText.CString(source, ref metadataOffset, source.Length);

        metadataOffset += 2;

        int prefixLength = nameOffset - table.Offset;
        byte[] name = Encoding.UTF8.GetBytes(secondaryName);
        byte[] changed = new byte[source.Length + prefixLength + name.Length + 3 + 82];
        int newMetadataOffset = source.Length + prefixLength + name.Length + 3;

        source.CopyTo(changed);
        source.Slice(table.Offset, prefixLength).CopyTo(changed.AsSpan(source.Length));
        name.CopyTo(changed.AsSpan(source.Length + prefixLength));
        source.Slice(metadataOffset - 2, 2).CopyTo(changed.AsSpan(newMetadataOffset - 2));
        source.Slice(metadataOffset, 82).CopyTo(changed.AsSpan(newMetadataOffset));

        int directoryOffset = (int)Binary.U64(source, 56);

        BinaryText.CString(source, ref directoryOffset, source.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(changed.AsSpan(directoryOffset), (ulong)source.Length);

        return changed;
    }

    private static DecodedSchema Parse(byte[] data, ushort version)
    {
        RepairChecksum(data);

        return SchemaReader.Read(data, new WindevOpenOptions(), version);
    }

    private static void RepairChecksum(byte[] data)
    {
        uint checksum = 0;
        int offset = 8;

        for (; offset <= data.Length - 4; offset += 4)
        {
            checksum = unchecked(checksum + BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));
        }

        for (; offset < data.Length; offset++)
        {
            checksum = unchecked(checksum + data[offset]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), checksum);
    }

    private static void RejectWithMessage(byte[] data, ushort version, string message, Action<bool, string> check)
    {
        try
        {
            Parse(data, version);
        }
        catch (WindevFormatException exception)
        {
            check(exception.Message.StartsWith(message, StringComparison.Ordinal), "The expected structural check rejects the malformed schema");

            return;
        }

        check(false, "Malformed schema must be rejected despite its divergent secondary name");
    }
}
