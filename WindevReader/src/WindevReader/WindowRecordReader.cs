using System.Buffers.Binary;
using WindevReader.Internal;

namespace WindevReader;

internal sealed record WindowControlDefinition(WindevString Name, WindevUiControlRecord Record);

internal static class WindowRecordReader
{
    internal static IReadOnlyList<WindowControlDefinition> Read(
        ReadOnlySpan<byte> data,
        ushort version,
        int maxStringBytes)
    {
        uint descriptor = DescriptorForVersion(version);
        uint commonDescriptor = version == 31 ? 610u : 607u;
        byte[] marker = new byte[4];
        List<WindowControlDefinition> definitions = new List<WindowControlDefinition>();
        int cursor = 8;

        BinaryPrimitives.WriteUInt32LittleEndian(marker, descriptor);

        while (cursor <= data.Length - 8)
        {
            int displacement = data[cursor..].IndexOf(marker);

            if (displacement < 0)
            {
                break;
            }

            int descriptorOffset = cursor + displacement;

            cursor = descriptorOffset + 4;

            if (!TryReadRecord(data, descriptorOffset - 8, data.Length, descriptor, out WindevUiControlRecord record))
            {
                continue;
            }

            WindevString? name = ReadName(data, record, commonDescriptor, maxStringBytes);

            if (name is null || !UiReader.IsIdentifier(name.Value))
            {
                continue;
            }

            WindevUiControlRecord decoded = version == 28
                ? WindowContentReader.Read(data, record, maxStringBytes)
                : record;

            definitions.Add(new WindowControlDefinition(name, decoded));
        }

        return definitions
            .GroupBy(definition => definition.Name.Offset)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
    }

    internal static uint DescriptorForVersion(ushort version) => version switch
    {
        24 => 623,
        25 => 624,
        28 => 628,
        29 => 629,
        31 => 631,
        _ => 0
    };

    internal static bool TryReadRecord(
        ReadOnlySpan<byte> data,
        int offset,
        int limit,
        uint descriptor,
        out WindevUiControlRecord record)
    {
        record = null!;

        if (offset < 0 || limit > data.Length || offset > limit - 16 || Binary.U32(data, offset + 8) != descriptor)
        {
            return false;
        }

        uint length = Binary.U32(data, offset);
        uint commonLength = Binary.U32(data, offset + 12);

        if (length < 16 || length > limit - offset || commonLength > length - 16)
        {
            return false;
        }

        record = new WindevUiControlRecord(
            offset,
            (int)length,
            Binary.U32(data, offset + 4),
            descriptor,
            offset + 16,
            (int)commonLength,
            offset + 16 + (int)commonLength);

        return true;
    }

    private static WindevString? ReadName(
        ReadOnlySpan<byte> data,
        WindevUiControlRecord record,
        uint descriptor,
        int maxStringBytes)
    {
        int end = record.SpecificPropertiesOffset;
        int cursor = record.CommonPropertiesOffset;
        WindevString? name = null;

        while (cursor <= end - 4)
        {
            if (Binary.U32(data, cursor) == descriptor &&
                BinaryText.TryString(data[..end], cursor + 4, maxStringBytes, out WindevString candidate))
            {
                if (name is not null || candidate.Offset + candidate.ByteLength > end - 77)
                {
                    return null;
                }

                name = candidate;
            }

            cursor++;
        }

        return name;
    }
}
