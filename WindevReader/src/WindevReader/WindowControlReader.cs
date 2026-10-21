using WindevReader.Internal;

namespace WindevReader;

internal static class WindowControlReader
{
    internal static IReadOnlyList<WindevUiElement> Read(
        IReadOnlyList<WindevUiElement> elements,
        IReadOnlyList<WindevDecodedBlock> buffers,
        ushort version,
        int maxStringBytes)
    {
        if (version is not (24 or 25 or 29 or 31))
        {
            return elements.DistinctBy(element => element.Name).ToArray();
        }

        List<WindevUiElement> result = new List<WindevUiElement>();

        foreach (WindevUiElement element in elements)
        {
            WindevDecodedBlock? buffer = buffers.FirstOrDefault(candidate => candidate.Offset == element.BufferOffset);
            WindevUiElement decoded = buffer is null
                ? element
                : ReadElement(element, buffer.Data.Span, version, maxStringBytes);

            result.Add(decoded);
        }

        return result
            .GroupBy(element => element.Name, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(element => element.Layout is not null) ?? group.First())
            .ToArray();
    }

    private static WindevUiElement ReadElement(
        WindevUiElement element,
        ReadOnlySpan<byte> data,
        ushort version,
        int maxStringBytes)
    {
        uint descriptor = version == 31 ? 610u : 607u;
        int descriptorOffset = element.Offset - 4;

        if (descriptorOffset < 0 || Binary.U32(data, descriptorOffset) != descriptor ||
            !BinaryText.TryString(data, element.Offset, maxStringBytes, out WindevString name) ||
            name.Value != element.Name)
        {
            return element;
        }

        int nameEnd = name.Offset + name.ByteLength;

        if (nameEnd > data.Length - 77)
        {
            return element;
        }

        WindevBounds bounds = new WindevBounds(
            unchecked((int)Binary.U32(data, nameEnd + 8)),
            unchecked((int)Binary.U32(data, nameEnd + 12)),
            unchecked((int)Binary.U32(data, nameEnd + 16)),
            unchecked((int)Binary.U32(data, nameEnd + 20)));

        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return element;
        }

        int labelOffset = nameEnd + 76;
        int? tailOffset = null;
        WindevString? caption = null;

        if (BinaryText.TryString(data, labelOffset, maxStringBytes, out WindevString label))
        {
            caption = label;
            tailOffset = labelOffset + label.ByteLength;
        }
        else if (data[labelOffset] == 0)
        {
            tailOffset = labelOffset + 1;
        }

        List<WindevUiRawValue> rawValues = new List<WindevUiRawValue>();

        for (int displacement = 24; displacement <= 72; displacement += 4)
        {
            int offset = nameEnd + displacement;

            rawValues.Add(new WindevUiRawValue(offset, 4, Binary.U32(data, offset)));
        }

        if (tailOffset is int tail && tail <= data.Length - 33)
        {
            foreach (int displacement in new int[] { 0, 4, 8, 12, 24, 29 })
            {
                int offset = tail + displacement;

                rawValues.Add(new WindevUiRawValue(offset, 4, Binary.U32(data, offset)));
            }

            if (caption is null && element.InferredKind == "Button")
            {
                int captionDisplacement = version switch
                {
                    24 => 160,
                    25 => 164,
                    29 => 195,
                    _ => -1
                };

                if (captionDisplacement >= 0 &&
                    BinaryText.TryString(data, tail + captionDisplacement, maxStringBytes, out WindevString buttonCaption))
                {
                    caption = buttonCaption;
                }
            }
        }

        WindevUiLayout layout = new WindevUiLayout(
            descriptorOffset,
            descriptor,
            nameEnd,
            Binary.U64(data, nameEnd),
            nameEnd + 8,
            caption?.Offset,
            tailOffset,
            rawValues.AsReadOnly());

        return element with
        {
            Bounds = bounds,
            Caption = caption?.Value,
            Layout = layout
        };
    }
}
