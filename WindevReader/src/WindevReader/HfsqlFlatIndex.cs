using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;
/// <summary>One key and two raw references. Both references match 1-based physical FIC slots in the observed 31 files.</summary>
public sealed record HfsqlIndexEntry(int Offset, ulong RawReference1, ulong RawReference2)
{
    [JsonIgnore]
    public ReadOnlyMemory<byte> Key
    {
        get; init;
    }
    public string KeyHex => Convert.ToHexString(Key.Span);
}

/// <summary>Observed clear, flat WINDEV 24/25/29/31 page. Terminal all-FF entry is validated and excluded from Entries.</summary>
public sealed class HfsqlFlatIndex
{
    public int Offset { get; }

    public int KeyByteLength { get; }

    public uint DeclaredEntryCount { get; }

    public int TerminalSentinelOffset { get; }

    public IReadOnlyList<HfsqlIndexEntry> Entries { get; }

    private HfsqlFlatIndex(int offset, int keyByteLength, uint declaredEntryCount, int sentinelOffset, List<HfsqlIndexEntry> entries)
    {
        Offset = offset;

        KeyByteLength = keyByteLength;

        DeclaredEntryCount = declaredEntryCount;

        TerminalSentinelOffset = sentinelOffset;

        Entries = entries.AsReadOnly();
    }

    internal static HfsqlFlatIndex Read(ReadOnlyMemory<byte> memory, HfsqlIndexDescriptor descriptor, int directoryOffset, int maxEntries)
    {
        ReadOnlySpan<byte> data = memory.Span;

        if (descriptor.RawPointerAt16 != ulong.MaxValue || descriptor.RawLengthAt24 != 0 || descriptor.RawPointerAt28 != (ulong)descriptor.Offset + 60)
        {
            throw new UnsupportedWindevFormatException("Unsupported NDX flat-page descriptor", descriptor.Offset);
        }

        int pageOffset = Binary.Offset(descriptor.RawPointerAt28, directoryOffset);
        int regionEnd = Binary.Offset(descriptor.RawPointerAt36, directoryOffset);

        if (pageOffset > regionEnd - 29)
        {
            throw new WindevFormatException("Truncated NDX flat-page header", pageOffset);
        }

        Binary.Require(data, pageOffset, 29);

        if (Binary.U16(data, pageOffset + 2) != 29 || data[pageOffset + 8] != 7 || Binary.U64(data, pageOffset + 13) != ulong.MaxValue || Binary.U64(data, pageOffset + 21) != ulong.MaxValue)
        {
            throw new UnsupportedWindevFormatException("Unsupported NDX flat-page header", pageOffset);
        }

        uint declaredEntryCount = Binary.U32(data, pageOffset + 9);

        if (declaredEntryCount != Binary.U32(data, pageOffset + 4) || declaredEntryCount == 0)
        {
            throw new WindevFormatException("NDX flat-page counts do not match the observed profile", pageOffset + 4);
        }

        if (declaredEntryCount - 1 > maxEntries)
        {
            throw new WindevFormatException("NDX flat-page entry budget exceeded", pageOffset + 9);
        }

        int keyByteLength = descriptor.RawWordAt14;
        int entryByteLength = keyByteLength + 16;

        if (keyByteLength == 0 || declaredEntryCount > (uint)((regionEnd - pageOffset - 29) / entryByteLength))
        {
            throw new WindevFormatException("NDX flat-page entries exceed declared region", pageOffset + 9);
        }

        int sentinelOffset = pageOffset + 29 + ((int)declaredEntryCount - 1) * entryByteLength;

        foreach (byte value in data.Slice(sentinelOffset, entryByteLength))
        {
            if (value != 255)
            {
                throw new WindevFormatException("Missing NDX all-FF terminal sentinel", sentinelOffset);
            }
        }

        List<HfsqlIndexEntry> entries = new List<HfsqlIndexEntry>();

        for (int index = 0; index < (int)declaredEntryCount - 1; index++)
        {
            int entryOffset = pageOffset + 29 + index * entryByteLength;
            ulong firstReference = Binary.U64(data, entryOffset + keyByteLength);
            ulong secondReference = Binary.U64(data, entryOffset + keyByteLength + 8);

            if (firstReference == ulong.MaxValue || secondReference == ulong.MaxValue)
            {
                throw new WindevFormatException("Unexpected reference sentinel inside flat entries", entryOffset + keyByteLength);
            }

            if (index > 0 && data.Slice(entryOffset - entryByteLength, keyByteLength).SequenceCompareTo(data.Slice(entryOffset, keyByteLength)) > 0)
            {
                throw new WindevFormatException("NDX flat-page keys are not ordered", entryOffset);
            }

            entries.Add(new HfsqlIndexEntry(entryOffset, firstReference, secondReference)
            {
                Key = memory.Slice(entryOffset, keyByteLength)
            });
        }

        return new HfsqlFlatIndex(pageOffset, keyByteLength, declaredEntryCount, sentinelOffset, entries);
    }
}
