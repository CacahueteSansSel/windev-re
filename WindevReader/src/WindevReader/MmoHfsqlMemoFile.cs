using System.Text;
using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;

public sealed class MmoHfsqlMemoFile : HfsqlPhysicalFile
{
    /// <summary>Fourteen raw u64 slots starting at offset 98 in the observed header profile; semantics are not mapped.</summary>
    public IReadOnlyList<ulong> RawHeaderSlots { get; }

    public ushort RawWordAt90 { get; }

    public ushort RawWordAt92 { get; }

    public uint RawWordAt94 { get; }

    public bool HasBytesAfterHeader => RawData.Length > 210;

    private readonly WindevOpenOptions readerOptions;

    private MmoHfsqlMemoFile(string file, byte[] data, WindevOpenOptions options) : base(file, data, HfsqlPhysicalFileKind.Memo)
    {
        readerOptions = options;

        RawWordAt90 = Binary.U16(data, 90);

        RawWordAt92 = Binary.U16(data, 92);

        RawWordAt94 = Binary.U32(data, 94);

        RawHeaderSlots = Array.AsReadOnly(Enumerable.Range(0, 14).Select(index => Binary.U64(data, 98 + index * 8)).ToArray());
    }

    /// <summary>Reads the observed uncompressed single-block memo. A reference is an absolute offset, not a slot number.</summary>
    public HfsqlMemoBlock ReadMemo(ulong reference)
    {
        if (Header.WindevVersion is not (15 or 24 or 25 or 29 or 31) || Header.RawModeAt88 != 0)
        {
            throw new UnsupportedWindevFormatException("Unsupported physical memo profile");
        }

        return HfsqlMemoBlock.Read(RawData, reference, readerOptions);
    }

    /// <summary>Also verifies that the memo belongs to the caller's FIC by both GUIDs.</summary>
    public HfsqlMemoBlock ReadMemo(FicHfsqlDataFile owner, ulong reference)
    {
        if (!HasSameIdentityAs(owner))
        {
            throw new WindevFormatException("FIC/MMO identities do not match");
        }

        return ReadMemo(reference);
    }

    public static MmoHfsqlMemoFile Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        return new MmoHfsqlMemoFile(filename, FileInput.Read(filename, 0x70003, options, 210), options);
    }
}
