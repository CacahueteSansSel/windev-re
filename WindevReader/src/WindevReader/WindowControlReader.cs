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
        if (version is not (24 or 25 or 28 or 29 or 31))
        {
            return elements.DistinctBy(element => element.Name).ToArray();
        }

        List<WindevUiElement> result = new List<WindevUiElement>();

        foreach (WindevDecodedBlock buffer in buffers)
        {
            IReadOnlyList<WindowControlDefinition> definitions = WindowRecordReader.Read(buffer.Data.Span, version, maxStringBytes);
            Dictionary<int, WindowControlDefinition> records = definitions.ToDictionary(definition => definition.Name.Offset);
            List<WindevUiElement> candidates = elements.Where(element => element.BufferOffset == buffer.Offset).ToList();
            HashSet<int> knownOffsets = candidates.Select(element => element.Offset).ToHashSet();

            foreach (WindowControlDefinition definition in definitions)
            {
                if (knownOffsets.Add(definition.Name.Offset))
                {
                    candidates.Add(new WindevUiElement(
                        definition.Name.Value,
                        UiReader.InferKind(definition.Name.Value),
                        buffer.Offset,
                        definition.Name.Offset,
                        Array.Empty<WindevString>()));
                }
            }

            foreach (WindevUiElement element in candidates)
            {
                records.TryGetValue(element.Offset, out WindowControlDefinition? definition);

                WindevUiElement decoded = ReadElement(element, buffer.Data.Span, version, maxStringBytes, definition?.Record);

                result.Add(decoded);
            }
        }

        return result
            .GroupBy(element => element.Name, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(element => element.ControlRecord is not null) ?? group.FirstOrDefault(element => element.Layout is not null) ?? group.First())
            .ToArray();
    }

    private static WindevUiElement ReadElement(
        WindevUiElement element,
        ReadOnlySpan<byte> data,
        ushort version,
        int maxStringBytes,
        WindevUiControlRecord? record)
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

        (int? stateOffset, int? labelOffset) = ReadHeaderPositions(data, nameEnd, version, maxStringBytes);
        WindevString? caption = null;
        int? tailOffset = null;

        if (labelOffset is int label)
        {
            if (BinaryText.TryString(data, label, maxStringBytes, out WindevString labelText))
            {
                caption = labelText;
                tailOffset = label + labelText.ByteLength;
            }
            else if (label < data.Length && data[label] == 0)
            {
                tailOffset = label + 1;
            }
        }

        List<WindevUiRawValue> rawValues = ReadRawValues(data, nameEnd, tailOffset);

        if (caption is null && tailOffset is int tail &&
            (record?.Type == WindevControlType.Button || record is null && element.InferredKind == "Button"))
        {
            caption = ReadButtonCaption(data, tail, version, maxStringBytes);
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
        WindevUiInteraction? interaction = version is 28 or 29 or 31
            ? ReadInteraction(data, nameEnd, stateOffset, tailOffset, maxStringBytes)
            : null;

        return element with
        {
            Bounds = bounds,
            Caption = caption?.Value,
            Layout = layout,
            Interaction = interaction,
            ControlRecord = record
        };
    }

    private static (int? StateOffset, int? LabelOffset) ReadHeaderPositions(
        ReadOnlySpan<byte> data,
        int nameEnd,
        ushort version,
        int maxStringBytes)
    {
        int cursor = nameEnd + 44;

        for (int slotIndex = 0; slotIndex < 3; slotIndex++)
        {
            if (!TrySkipTextSlot(data, cursor, maxStringBytes, out cursor))
            {
                return (null, null);
            }
        }

        if (cursor > data.Length - 4)
        {
            return (null, null);
        }

        int stateOffset = cursor;

        if (!TrySkipTextSlot(data, stateOffset + 4, maxStringBytes, out int textEnd))
        {
            return (stateOffset, null);
        }

        int labelDisplacement = version == 31 ? 28 : 24;

        if (textEnd >= data.Length - labelDisplacement)
        {
            return (stateOffset, null);
        }

        return (stateOffset, textEnd + labelDisplacement);
    }

    private static bool TrySkipTextSlot(
        ReadOnlySpan<byte> data,
        int offset,
        int maxStringBytes,
        out int endOffset)
    {
        endOffset = offset;

        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        if (data[offset] == 0)
        {
            endOffset = offset + 1;

            return true;
        }

        if (!BinaryText.TryString(data, offset, maxStringBytes, out WindevString text))
        {
            return false;
        }

        endOffset = offset + text.ByteLength;

        return true;
    }

    private static List<WindevUiRawValue> ReadRawValues(
        ReadOnlySpan<byte> data,
        int nameEnd,
        int? tailOffset)
    {
        List<WindevUiRawValue> values = new List<WindevUiRawValue>();

        for (int displacement = 24; displacement <= 72; displacement += 4)
        {
            int offset = nameEnd + displacement;

            values.Add(new WindevUiRawValue(offset, 4, Binary.U32(data, offset)));
        }

        if (tailOffset is int tail && tail <= data.Length - 33)
        {
            foreach (int displacement in new int[] { 0, 4, 8, 12, 24, 29 })
            {
                int offset = tail + displacement;

                values.Add(new WindevUiRawValue(offset, 4, Binary.U32(data, offset)));
            }
        }

        return values;
    }

    private static WindevString? ReadButtonCaption(
        ReadOnlySpan<byte> data,
        int tailOffset,
        ushort version,
        int maxStringBytes)
    {
        int displacement = version switch
        {
            24 => 160,
            25 => 164,
            28 => 194,
            29 => 195,
            _ => -1
        };

        return displacement >= 0 && tailOffset <= data.Length - displacement &&
            BinaryText.TryString(data, tailOffset + displacement, maxStringBytes, out WindevString caption)
            ? caption
            : null;
    }

    private static WindevUiInteraction ReadInteraction(
        ReadOnlySpan<byte> data,
        int nameEnd,
        int? stateOffset,
        int? tailOffset,
        int maxStringBytes)
    {
        int? tabAccessOffset = null;
        uint? rawTabAccess = null;

        if (tailOffset is int tail && tail <= data.Length - 4 &&
            TrySkipTextSlot(data, tail + 4, maxStringBytes, out int valueEnd) &&
            valueEnd <= data.Length - 7)
        {
            tabAccessOffset = valueEnd + 3;
            rawTabAccess = Binary.U32(data, tabAccessOffset.Value);
        }

        return new WindevUiInteraction(
            nameEnd + 40,
            Binary.U32(data, nameEnd + 40),
            stateOffset,
            stateOffset is int state ? Binary.U32(data, state) : null,
            tabAccessOffset,
            rawTabAccess);
    }
}
