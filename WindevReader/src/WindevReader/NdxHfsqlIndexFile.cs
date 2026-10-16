using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class NdxHfsqlIndexFile : HfsqlPhysicalFile
{
    public ushort DeclaredIndexCount { get; }

    public int DirectoryOffset { get; }

    /// <summary>Positions read from the terminal u64 directory. Index key/B-tree semantics remain unsupported.</summary>
    public IReadOnlyList<int> IndexDescriptorOffsets { get; }

    /// <summary>60-byte descriptors referenced by the terminal directory. Pointers remain raw structural observations.</summary>
    public IReadOnlyList<HfsqlIndexDescriptor> IndexDescriptors { get; }

    private readonly WindevOpenOptions readerOptions;

    private NdxHfsqlIndexFile(string file, byte[] data, WindevOpenOptions options) : base(file, data, HfsqlPhysicalFileKind.Index)
    {
        readerOptions = options;

        DeclaredIndexCount = Binary.U16(data, 107);

        DirectoryOffset = Binary.Offset(Binary.U64(data, 111), data.Length);

        if (DirectoryOffset < 119 || (long)DirectoryOffset + (long)DeclaredIndexCount * 8 != data.Length)
        {
            throw new WindevFormatException("Unsupported or truncated NDX terminal directory", 111);
        }

        List<int> positions = new List<int>();
        int previous = 118;

        for (int index = 0; index < DeclaredIndexCount; index++)
        {
            int position = Binary.Offset(Binary.U64(data, DirectoryOffset + index * 8), data.Length);

            if (position <= previous || position >= DirectoryOffset)
            {
                throw new WindevFormatException("NDX descriptor positions are not ordered inside the body", DirectoryOffset + index * 8);
            }

            positions.Add(position);

            previous = position;
        }

        IndexDescriptorOffsets = positions.AsReadOnly();

        List<HfsqlIndexDescriptor> descriptors = new List<HfsqlIndexDescriptor>();

        foreach (int position in positions)
        {
            Binary.Require(data, position, 60);

            if (position > DirectoryOffset - 60 || Binary.U32(data, position + 2) != 60)
            {
                throw new UnsupportedWindevFormatException("Unsupported NDX descriptor size", position + 2);
            }

            descriptors.Add(new HfsqlIndexDescriptor(
                position,
                Binary.U16(data, position),
                Binary.U32(data, position + 6),
                Binary.U32(data, position + 10),
                Binary.U16(data, position + 14),
                Binary.U64(data, position + 16),
                Binary.U32(data, position + 24),
                Binary.U64(data, position + 28),
                Binary.U64(data, position + 36),
                Array.AsReadOnly(Enumerable.Range(0, 4).Select(index2 => Binary.U32(data, position + 44 + 4 * index2)).ToArray())));
        }

        IndexDescriptors = descriptors.AsReadOnly();
    }

    public static NdxHfsqlIndexFile Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new NdxHfsqlIndexFile(filename, FileInput.Read(filename, 0x70002, options, 119), options);
    }

    /// <summary>Reads the observed clear WINDEV 15/24/25/29/31 flat page (tag 7, 29-byte header). No B-tree traversal or key-name inference.</summary>
    public HfsqlFlatIndex ReadFlatIndex(int index)
    {
        if ((uint)index >= (uint)IndexDescriptors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (Header.WindevVersion is not (15 or 24 or 25 or 29 or 31) || Header.RawModeAt88 != 0)
        {
            throw new UnsupportedWindevFormatException("Clear flat index profile was validated with WINDEV 15/24/25/29/31 mode 0 only");
        }

        HfsqlIndexDescriptor descriptor = IndexDescriptors[index];

        return HfsqlFlatIndex.Read(RawData, descriptor, DirectoryOffset, readerOptions.MaxIndexEntries);
    }

    /// <summary>Bytes between an announced descriptor and the next one (or the directory), without B-tree interpretation.</summary>
    public ReadOnlyMemory<byte> ReadIndexRegion(int index)
    {
        if (index < 0 || index >= IndexDescriptorOffsets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int start = IndexDescriptorOffsets[index];
        int end = index + 1 < IndexDescriptorOffsets.Count ? IndexDescriptorOffsets[index + 1] : DirectoryOffset;

        return RawData.Slice(start, end - start);
    }
}
