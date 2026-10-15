using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

/// <summary>One key and two raw references. Both references match 1-based physical FIC slots in the observed 31 files.</summary>
public sealed record HfsqlIndexEntry(int Offset, ulong RawReference1, ulong RawReference2)
{
    [JsonIgnore] public ReadOnlyMemory<byte> Key { get; init; }
    public string KeyHex => Convert.ToHexString(Key.Span);
}

/// <summary>Observed clear, flat WINDEV 31 page. Terminal all-FF entry is validated and excluded from Entries.</summary>
public sealed class HfsqlFlatIndex
{
    public int Offset { get; }
    public int KeyByteLength { get; }
    public uint DeclaredEntryCount { get; }
    public int TerminalSentinelOffset { get; }
    public IReadOnlyList<HfsqlIndexEntry> Entries { get; }
    private HfsqlFlatIndex(int offset, int width, uint count, int sentinel, List<HfsqlIndexEntry> entries)
    { Offset = offset; KeyByteLength = width; DeclaredEntryCount = count; TerminalSentinelOffset = sentinel; Entries = entries.AsReadOnly(); }

    internal static HfsqlFlatIndex Read(ReadOnlyMemory<byte> memory, HfsqlIndexDescriptor descriptor, int directoryOffset, int maxEntries)
    {
        var data = memory.Span;
        if (descriptor.RawPointerAt16 != ulong.MaxValue || descriptor.RawLengthAt24 != 0 ||
            descriptor.RawPointerAt28 != (ulong)descriptor.Offset + 60)
            throw new UnsupportedWindevFormatException("Unsupported NDX flat-page descriptor", descriptor.Offset);
        int start = Binary.Offset(descriptor.RawPointerAt28, directoryOffset);
        int end = Binary.Offset(descriptor.RawPointerAt36, directoryOffset);
        if (start > end - 29) throw new WindevFormatException("Truncated NDX flat-page header", start);
        Binary.Require(data, start, 29);
        if (Binary.U16(data, start + 2) != 29 || data[start + 8] != 7 ||
            Binary.U64(data, start + 13) != ulong.MaxValue || Binary.U64(data, start + 21) != ulong.MaxValue)
            throw new UnsupportedWindevFormatException("Unsupported NDX flat-page header", start);
        uint count = Binary.U32(data, start + 9);
        if (count != Binary.U32(data, start + 4) || count == 0)
            throw new WindevFormatException("NDX flat-page counts do not match the observed profile", start + 4);
        if (count - 1 > maxEntries) throw new WindevFormatException("NDX flat-page entry budget exceeded", start + 9);
        int width = descriptor.RawWordAt14, stride = width + 16;
        if (width == 0 || count > (uint)((end - start - 29) / stride))
            throw new WindevFormatException("NDX flat-page entries exceed declared region", start + 9);
        int sentinel = start + 29 + ((int)count - 1) * stride;
        foreach (byte value in data.Slice(sentinel, stride))
            if (value != 255) throw new WindevFormatException("Missing NDX all-FF terminal sentinel", sentinel);
        var entries = new List<HfsqlIndexEntry>();
        for (int i = 0; i < (int)count - 1; i++)
        {
            int p = start + 29 + i * stride;
            ulong first = Binary.U64(data, p + width), second = Binary.U64(data, p + width + 8);
            if (first == ulong.MaxValue || second == ulong.MaxValue)
                throw new WindevFormatException("Unexpected reference sentinel inside flat entries", p + width);
            if (i > 0 && data.Slice(p - stride, width).SequenceCompareTo(data.Slice(p, width)) > 0)
                throw new WindevFormatException("NDX flat-page keys are not ordered", p);
            entries.Add(new(p, first, second) { Key = memory.Slice(p, width) });
        }
        return new(start, width, count, sentinel, entries);
    }
}
