using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class FicHfsqlDataFile : HfsqlPhysicalFile
{
    private FicHfsqlDataFile(string file, byte[] data) : base(file, data, HfsqlPhysicalFileKind.Data)
    {
    }

    /// <summary>Reads physical slots with a caller-supplied layout; does not infer schema, decrypt, or filter deleted records.</summary>
    public HfsqlFixedRecordView ReadFixedRecords(HfsqlFixedRecordLayout layout) => new(RawData, layout);

    /// <summary>Uses a matching WINDEV 15/24/25/28/29/31 WDD to name/type fixed fields. Data offset and physical count remain caller-supplied.
    /// Only the observed 9-byte-prefix, fixed-field profile is supported. Does not classify live/deleted records.</summary>
    public HfsqlFixedRecordLayout CreateFixedRecordLayout(WddWindevAnalysis analysis, int dataOffset, int recordCount)
    {
        WindevDataTable table = FindTableDefinition(analysis) ?? throw new WindevFormatException("WDD analysis/table GUIDs do not match this FIC");

        if (Header.WindevVersion is not (15 or 24 or 25 or 28 or 29 or 31) || analysis.Header.WindevVersion != Header.WindevVersion)
        {
            throw new UnsupportedWindevFormatException("Schema-assisted fixed layout requires matching WINDEV 15, 24, 25, 28, 29 or 31 profiles");
        }

        List<HfsqlFixedField> fields = new List<HfsqlFixedField>();
        int offset = 9;

        foreach (WindevDataField field in table.Fields)
        {
            // Recognized composite keys describe index values rather than additional inline record fields.
            if (analysis.Header.WindevVersion is 28 or 31 && field.IsCompositeKey)
            {
                continue;
            }

            HfsqlFixedFieldType type = (field.RawTypeCode, field.StorageSize) switch
            {
                (38, 4) when analysis.Header.WindevVersion == 15 => HfsqlFixedFieldType.Int32,
                (29, 8) => HfsqlFixedFieldType.UInt64, // Memo reference, not inline text.
                (24, 8) or (25, 8) => HfsqlFixedFieldType.Int64,
                (27, 8) => HfsqlFixedFieldType.UInt64,
                (2, > 0) when field.StorageSize == field.Size + 1 => HfsqlFixedFieldType.Cp1252Z,
                (5, 4) => HfsqlFixedFieldType.Int32,
                (37, 1) => HfsqlFixedFieldType.Boolean8,
                (36, 1) => HfsqlFixedFieldType.UInt8,
                (14, 9) when field.Size == 8 => HfsqlFixedFieldType.Date8Z,
                _ => throw new UnsupportedWindevFormatException($"Unsupported schema-assisted physical field: {field.Name}")
            };

            fields.Add(new HfsqlFixedField(field.Name, offset, field.StorageSize, type));

            offset = checked(offset + field.StorageSize);
        }

        if (table.RawRecordByteLength != offset)
        {
            throw new UnsupportedWindevFormatException("WDD slot length does not match the observed fixed-field profile");
        }

        HfsqlFixedRecordLayout layout = new HfsqlFixedRecordLayout(dataOffset, offset, recordCount, fields.AsReadOnly());

        _ = ReadFixedRecords(layout); // Validate physical boundaries before returning a usable layout.

        return layout;
    }

    public static FicHfsqlDataFile Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new FicHfsqlDataFile(filename, FileInput.Read(filename, 0x70001, options, 90));
    }
}
