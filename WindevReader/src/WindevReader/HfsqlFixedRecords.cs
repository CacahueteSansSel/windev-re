using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace WindevReader;
/// <summary>Explicit interpretation selected by the caller, not a recovered HFSQL schema type code.</summary>
public enum HfsqlFixedFieldType
{
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Cp1252Z,
    Utf16LeZ,
    Bytes,
    Boolean8,
    Date8Z
}

public sealed record HfsqlFixedField(string Name, int Offset, int ByteLength, HfsqlFixedFieldType Type);
/// <summary>Physical slot boundaries and fields supplied by the caller. Offsets of fields are relative to each slot.</summary>
public sealed record HfsqlFixedRecordLayout(int DataOffset, int RecordByteLength, int RecordCount, IReadOnlyList<HfsqlFixedField> Fields);
public sealed record HfsqlFixedRecord(int SlotIndex, int Offset, IReadOnlyDictionary<string, object> Values)
{
    [JsonIgnore]
    public ReadOnlyMemory<byte> RawData
    {
        get; init;
    }
}

/// <summary>Lazy, bounded physical access. Does not infer whether a slot is active, deleted, erased or a template.</summary>
public sealed class HfsqlFixedRecordView : IReadOnlyList<HfsqlFixedRecord>
{
    private readonly ReadOnlyMemory<byte> data;

    private static readonly Encoding utf16 = new UnicodeEncoding(false, false, true);

    private const string cp1252 = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";

    public HfsqlFixedRecordLayout Layout { get; }

    public int Count => Layout.RecordCount;

    internal HfsqlFixedRecordView(ReadOnlyMemory<byte> bytes, HfsqlFixedRecordLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        ArgumentNullException.ThrowIfNull(layout.Fields);

        if (layout.DataOffset < 90 || layout.RecordByteLength <= 0 || layout.RecordCount < 0 || (long)layout.DataOffset + (long)layout.RecordByteLength * layout.RecordCount > bytes.Length)
        {
            throw new WindevFormatException("Explicit FIC layout exceeds file boundaries");
        }

        HfsqlFixedField[] fields = layout.Fields.ToArray();
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        foreach (HfsqlFixedField field in fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name) || field.Offset < 0 || field.ByteLength <= 0 || field.Offset > layout.RecordByteLength - field.ByteLength)
            {
                throw new WindevFormatException("Invalid, duplicate or out-of-slot explicit field");
            }

            int width = field.Type switch
            {
                HfsqlFixedFieldType.UInt8 or HfsqlFixedFieldType.Boolean8 => 1,
                HfsqlFixedFieldType.Date8Z => 9,
                HfsqlFixedFieldType.Int16 or HfsqlFixedFieldType.UInt16 => 2,
                HfsqlFixedFieldType.Int32 or HfsqlFixedFieldType.UInt32 => 4,
                HfsqlFixedFieldType.Int64 or HfsqlFixedFieldType.UInt64 => 8,
                HfsqlFixedFieldType.Cp1252Z or HfsqlFixedFieldType.Utf16LeZ or HfsqlFixedFieldType.Bytes => 0,
                _ => throw new UnsupportedWindevFormatException("Unsupported explicit FIC field type")
            };

            if ((width != 0 && width != field.ByteLength) || (field.Type == HfsqlFixedFieldType.Utf16LeZ && field.ByteLength % 2 != 0))
            {
                throw new WindevFormatException("Explicit field width does not match its type");
            }
        }

        Layout = layout with
        {
            Fields = Array.AsReadOnly(fields)
        };

        data = bytes;
    }

    public HfsqlFixedRecord this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int offset = Layout.DataOffset + index * Layout.RecordByteLength;
            ReadOnlyMemory<byte> rawData = data.Slice(offset, Layout.RecordByteLength);
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (HfsqlFixedField field in Layout.Fields)
            {
                values.Add(field.Name, Decode(rawData.Span.Slice(field.Offset, field.ByteLength), field.Type, offset + field.Offset));
            }

            return new HfsqlFixedRecord(index, offset, new ReadOnlyDictionary<string, object>(values))
            {
                RawData = rawData
            };
        }
    }

    private static object Decode(ReadOnlySpan<byte> bytes, HfsqlFixedFieldType type, int offset)
    {
        switch (type)
        {
            case HfsqlFixedFieldType.UInt8:
                return bytes[0];
            case HfsqlFixedFieldType.Boolean8:
                if (bytes[0] > 1)
                {
                    throw new WindevFormatException("Non-boolean value in explicit Boolean8 field", offset);
                }

                return bytes[0] != 0;
            case HfsqlFixedFieldType.Int16:
                return BinaryPrimitives.ReadInt16LittleEndian(bytes);
            case HfsqlFixedFieldType.UInt16:
                return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            case HfsqlFixedFieldType.Int32:
                return BinaryPrimitives.ReadInt32LittleEndian(bytes);
            case HfsqlFixedFieldType.UInt32:
                return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            case HfsqlFixedFieldType.Int64:
                return BinaryPrimitives.ReadInt64LittleEndian(bytes);
            case HfsqlFixedFieldType.UInt64:
                return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            case HfsqlFixedFieldType.Bytes:
                return bytes.ToArray();
            case HfsqlFixedFieldType.Date8Z:
                if (bytes[8] != 0)
                {
                    throw new WindevFormatException("Unterminated Date8Z field", offset);
                }

                foreach (byte character in bytes[..8])
                {
                    if (character is < (byte)'0' or > (byte)'9')
                    {
                        throw new WindevFormatException("Non-digit Date8Z field", offset);
                    }
                }

                return Encoding.ASCII.GetString(bytes[..8]);
            case HfsqlFixedFieldType.Cp1252Z:
                int end = bytes.IndexOf((byte)0);
                if (end < 0)
                {
                    throw new WindevFormatException("Unterminated explicit CP1252 field", offset);
                }

                char[] characters = new char[end];
                for (int index = 0; index < end; index++)
                {
                    characters[index] = bytes[index] is >= 128 and < 160 ? cp1252[bytes[index] - 128] : (char)bytes[index];
                }

                return new string(characters);
            case HfsqlFixedFieldType.Utf16LeZ:
                for (int codeUnitOffset = 0; codeUnitOffset < bytes.Length; codeUnitOffset += 2)
                {
                    if (bytes[codeUnitOffset] == 0 && bytes[codeUnitOffset + 1] == 0)
                    {
                        try
                        {
                            return utf16.GetString(bytes[..codeUnitOffset]);
                        }
                        catch (DecoderFallbackException)
                        {
                            throw new WindevFormatException("Invalid explicit UTF-16 field", offset);
                        }
                    }
                }

                throw new WindevFormatException("Unterminated explicit UTF-16 field", offset);
            default:
                throw new UnsupportedWindevFormatException("Unsupported explicit FIC field type");
        }
    }

    public IEnumerator<HfsqlFixedRecord> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
