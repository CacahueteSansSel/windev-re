using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace WindevReader;

/// <summary>Explicit interpretation selected by the caller, not a recovered HFSQL schema type code.</summary>
public enum HfsqlFixedFieldType { UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, Cp1252Z, Utf16LeZ, Bytes, Boolean8, Date8Z }
public sealed record HfsqlFixedField(string Name, int Offset, int ByteLength, HfsqlFixedFieldType Type);
/// <summary>Physical slot boundaries and fields supplied by the caller. Offsets of fields are relative to each slot.</summary>
public sealed record HfsqlFixedRecordLayout(int DataOffset, int RecordByteLength, int RecordCount,
    IReadOnlyList<HfsqlFixedField> Fields);
public sealed record HfsqlFixedRecord(int SlotIndex, int Offset, IReadOnlyDictionary<string, object> Values)
{
    [JsonIgnore] public ReadOnlyMemory<byte> RawData { get; init; }
}

/// <summary>Lazy, bounded physical access. Does not infer whether a slot is active, deleted, erased or a template.</summary>
public sealed class HfsqlFixedRecordView : IReadOnlyList<HfsqlFixedRecord>
{
    private readonly ReadOnlyMemory<byte> data;
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    private const string Cp1252 = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
    public HfsqlFixedRecordLayout Layout { get; }
    public int Count => Layout.RecordCount;
    internal HfsqlFixedRecordView(ReadOnlyMemory<byte> bytes, HfsqlFixedRecordLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(layout.Fields);
        if (layout.DataOffset < 90 || layout.RecordByteLength <= 0 || layout.RecordCount < 0 ||
            (long)layout.DataOffset + (long)layout.RecordByteLength * layout.RecordCount > bytes.Length)
            throw new WindevFormatException("Explicit FIC layout exceeds file boundaries");
        var fields = layout.Fields.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Name) || !names.Add(f.Name) || f.Offset < 0 ||
                f.ByteLength <= 0 || f.Offset > layout.RecordByteLength - f.ByteLength)
                throw new WindevFormatException("Invalid, duplicate or out-of-slot explicit field");
            int width = f.Type switch
            {
                HfsqlFixedFieldType.UInt8 or HfsqlFixedFieldType.Boolean8 => 1,
                HfsqlFixedFieldType.Date8Z => 9,
                HfsqlFixedFieldType.Int16 or HfsqlFixedFieldType.UInt16 => 2,
                HfsqlFixedFieldType.Int32 or HfsqlFixedFieldType.UInt32 => 4,
                HfsqlFixedFieldType.Int64 or HfsqlFixedFieldType.UInt64 => 8,
                HfsqlFixedFieldType.Cp1252Z or HfsqlFixedFieldType.Utf16LeZ or HfsqlFixedFieldType.Bytes => 0,
                _ => throw new UnsupportedWindevFormatException("Unsupported explicit FIC field type")
            };
            if ((width != 0 && width != f.ByteLength) ||
                (f.Type == HfsqlFixedFieldType.Utf16LeZ && f.ByteLength % 2 != 0))
                throw new WindevFormatException("Explicit field width does not match its type");
        }
        Layout = layout with { Fields = Array.AsReadOnly(fields) }; data = bytes;
    }
    public HfsqlFixedRecord this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            int offset = Layout.DataOffset + index * Layout.RecordByteLength;
            var raw = data.Slice(offset, Layout.RecordByteLength);
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var f in Layout.Fields)
                values.Add(f.Name, Decode(raw.Span.Slice(f.Offset, f.ByteLength), f.Type, offset + f.Offset));
            return new(index, offset, new ReadOnlyDictionary<string, object>(values)) { RawData = raw };
        }
    }
    private static object Decode(ReadOnlySpan<byte> b, HfsqlFixedFieldType type, int offset)
    {
        switch (type)
        {
            case HfsqlFixedFieldType.UInt8: return b[0];
            case HfsqlFixedFieldType.Boolean8:
                if (b[0] > 1) throw new WindevFormatException("Non-boolean value in explicit Boolean8 field", offset);
                return b[0] != 0;
            case HfsqlFixedFieldType.Int16: return BinaryPrimitives.ReadInt16LittleEndian(b);
            case HfsqlFixedFieldType.UInt16: return BinaryPrimitives.ReadUInt16LittleEndian(b);
            case HfsqlFixedFieldType.Int32: return BinaryPrimitives.ReadInt32LittleEndian(b);
            case HfsqlFixedFieldType.UInt32: return BinaryPrimitives.ReadUInt32LittleEndian(b);
            case HfsqlFixedFieldType.Int64: return BinaryPrimitives.ReadInt64LittleEndian(b);
            case HfsqlFixedFieldType.UInt64: return BinaryPrimitives.ReadUInt64LittleEndian(b);
            case HfsqlFixedFieldType.Bytes: return b.ToArray();
            case HfsqlFixedFieldType.Date8Z:
                if (b[8] != 0) throw new WindevFormatException("Unterminated Date8Z field", offset);
                foreach (byte c in b[..8])
                    if (c is < (byte)'0' or > (byte)'9') throw new WindevFormatException("Non-digit Date8Z field", offset);
                return Encoding.ASCII.GetString(b[..8]);
            case HfsqlFixedFieldType.Cp1252Z:
                int end = b.IndexOf((byte)0);
                if (end < 0) throw new WindevFormatException("Unterminated explicit CP1252 field", offset);
                var chars = new char[end];
                for (int i = 0; i < end; i++) chars[i] = b[i] is >= 128 and < 160 ? Cp1252[b[i] - 128] : (char)b[i];
                return new string(chars);
            case HfsqlFixedFieldType.Utf16LeZ:
                for (int i = 0; i < b.Length; i += 2)
                    if (b[i] == 0 && b[i + 1] == 0)
                    {
                        try { return Utf16.GetString(b[..i]); }
                        catch (DecoderFallbackException) { throw new WindevFormatException("Invalid explicit UTF-16 field", offset); }
                    }
                throw new WindevFormatException("Unterminated explicit UTF-16 field", offset);
            default: throw new UnsupportedWindevFormatException("Unsupported explicit FIC field type");
        }
    }
    public IEnumerator<HfsqlFixedRecord> GetEnumerator()
    { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
