using WindevReader.Internal;

namespace WindevReader;

internal static class WindowContentReader
{
    private const int maximumItemCount = 10_000;

    internal static WindevUiControlRecord Read(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        ReadOnlySpan<byte> bounded = data[..(record.Offset + record.ByteLength)];

        try
        {
            return record.Type switch
            {
                WindevControlType.ComboBox => ReadCombo(bounded, record, maxStringBytes),
                WindevControlType.CheckBox or WindevControlType.RadioButton => ReadChoices(bounded, record, maxStringBytes),
                WindevControlType.Image => ReadImage(bounded, record, maxStringBytes),
                WindevControlType.Table => ReadTable(bounded, record, maxStringBytes),
                WindevControlType.Tab => ReadTab(bounded, record, maxStringBytes),
                _ => record
            };
        }
        catch (WindevFormatException)
        {
            // Optional content must not turn an unsupported local layout into a failed window open.
            return record;
        }
    }

    private static WindevUiControlRecord ReadCombo(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        if (!WindowRecordReader.TryReadRecord(data, record.SpecificPropertiesOffset, data.Length, 628, out WindevUiControlRecord list) ||
            list.RawTypeCode != 7 || list.Offset + list.ByteLength != data.Length)
        {
            return record;
        }

        int body = list.SpecificPropertiesOffset;

        if (Binary.U32(data, body) != 0 || Binary.U32(data, body + 4) != 1 || Binary.U32(data, body + 8) != 0 ||
            !BinaryText.TryString(data, body + 12, maxStringBytes, out WindevString text))
        {
            return record;
        }

        List<WindevUiItem> items = new List<WindevUiItem>();
        int start = text.Offset + 8;
        int end = text.Offset + text.ByteLength - 5;

        for (int cursor = start; text.ByteLength > 13 && cursor <= end; cursor++)
        {
            if (cursor == end || data[cursor] == 10)
            {
                if (items.Count == maximumItemCount)
                {
                    return record;
                }

                items.Add(new WindevUiItem(BinaryText.Decode(data[start..cursor]), start, cursor - start, null, null));

                start = cursor + 1;
            }
        }

        return record with { Items = items.AsReadOnly(), ItemsDecoded = true, RawItemsText = text };
    }

    private static WindevUiControlRecord ReadChoices(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        int body = record.SpecificPropertiesOffset;

        if (Binary.U32(data, body) != 1)
        {
            return record;
        }

        int count = Binary.Count(Binary.U32(data, body + 4), maximumItemCount, body + 4);
        int cursor = body + 8;
        List<WindevUiItem> items = new List<WindevUiItem>();

        for (int index = 0; index < count; index++)
        {
            if (!BinaryText.TryString(data, cursor, maxStringBytes, out WindevString text))
            {
                return record;
            }

            int metadataOffset = cursor + text.ByteLength;

            Binary.Require(data, metadataOffset, 45);
            items.Add(new WindevUiItem(text.Value, text.Offset + 8, text.ByteLength - 13, cursor, metadataOffset));

            cursor = metadataOffset + 45;
        }

        return record with { Items = items.AsReadOnly(), ItemsDecoded = true };
    }

    private static WindevUiControlRecord ReadImage(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        return BinaryText.TryString(data, record.SpecificPropertiesOffset + 20, maxStringBytes, out WindevString path)
            ? record with { ImageSource = path }
            : record;
    }

    private static WindevUiControlRecord ReadTable(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        if (!BinaryText.TryString(data, record.SpecificPropertiesOffset + 13, maxStringBytes, out WindevString tableName) ||
            !BinaryText.TryString(data, tableName.Offset + tableName.ByteLength, maxStringBytes, out WindevString browseKey))
        {
            return record;
        }

        int keyEnd = browseKey.Offset + browseKey.ByteLength;
        uint count = Binary.U32(data, keyEnd + 174);

        Binary.Count(count, maximumItemCount, keyEnd + 174);

        List<WindevUiTableColumn> columns = new List<WindevUiTableColumn>();
        int cursor = keyEnd + 178;

        while (cursor <= data.Length - 4)
        {
            if (Binary.U32(data, cursor) == 621 &&
                TryReadColumn(data, cursor, maxStringBytes, out WindevUiTableColumn column))
            {
                if (columns.Count >= count)
                {
                    return record with { TableBinding = new WindevUiTableBinding(tableName, browseKey, count, Array.Empty<WindevUiTableColumn>()) };
                }

                columns.Add(column);
            }

            cursor++;
        }

        IReadOnlyList<WindevUiTableColumn> verified = columns.Count == count
            ? columns.AsReadOnly()
            : Array.Empty<WindevUiTableColumn>();

        return record with { TableBinding = new WindevUiTableBinding(tableName, browseKey, count, verified) };
    }

    private static bool TryReadColumn(ReadOnlySpan<byte> data, int descriptorOffset, int maxStringBytes, out WindevUiTableColumn column)
    {
        column = null!;

        if (!BinaryText.TryString(data, descriptorOffset + 4, maxStringBytes, out WindevString name))
        {
            return false;
        }

        int duplicateOffset = descriptorOffset - name.ByteLength;

        if (!BinaryText.TryString(data, duplicateOffset, maxStringBytes, out WindevString duplicate) ||
            duplicate.Value != name.Value || duplicate.Offset + duplicate.ByteLength != descriptorOffset)
        {
            return false;
        }

        int nameEnd = name.Offset + name.ByteLength;

        if (!BinaryText.TryString(data, nameEnd + 12, maxStringBytes, out WindevString caption) ||
            !BinaryText.TryString(data, caption.Offset + caption.ByteLength + 32, maxStringBytes, out WindevString binding))
        {
            return false;
        }

        int bindingEnd = binding.Offset + binding.ByteLength;
        uint editor = Binary.U32(data, bindingEnd);

        if (editor != Binary.U32(data, bindingEnd + 4))
        {
            return false;
        }

        column = new WindevUiTableColumn(
            name,
            descriptorOffset,
            Binary.U64(data, nameEnd),
            Binary.U32(data, nameEnd + 8),
            caption,
            binding,
            editor);

        return true;
    }

    private static WindevUiControlRecord ReadTab(ReadOnlySpan<byte> data, WindevUiControlRecord record, int maxStringBytes)
    {
        int body = record.SpecificPropertiesOffset;
        int count = Binary.Count(Binary.U32(data, body + 16), maximumItemCount, body + 16);
        int cursor = body + 20;
        int totalChildren = 0;
        List<WindevUiTabPage> pages = new List<WindevUiTabPage>();

        for (int pageIndex = 0; pageIndex < count; pageIndex++)
        {
            int pageOffset = cursor;

            if (Binary.U32(data, cursor) != 603 ||
                !BinaryText.TryString(data, cursor + 4, maxStringBytes, out WindevString caption))
            {
                return record;
            }

            cursor = caption.Offset + caption.ByteLength;

            int childCount = Binary.Count(Binary.U32(data, cursor + 18), maximumItemCount - totalChildren, cursor + 18);
            List<int> children = new List<int>();

            totalChildren += childCount;
            cursor += 22;

            for (int childIndex = 0; childIndex < childCount; childIndex++)
            {
                if (!WindowRecordReader.TryReadRecord(data, cursor, data.Length, 628, out WindevUiControlRecord child))
                {
                    return record;
                }

                children.Add(cursor);

                cursor += child.ByteLength;
            }

            if (Binary.U32(data, cursor) != 12)
            {
                return record;
            }

            ulong identifier = Binary.U64(data, cursor + 4);

            pages.Add(new WindevUiTabPage(pageOffset, caption, identifier, children.AsReadOnly()));

            cursor += 12;
        }

        return record with { TabPages = pages.AsReadOnly(), TabPagesDecoded = true };
    }
}
