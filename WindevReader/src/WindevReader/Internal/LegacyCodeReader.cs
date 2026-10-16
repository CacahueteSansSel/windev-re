namespace WindevReader.Internal;

internal static class LegacyCodeReader
{
    private const int eventHeaderLength = 21;
    private const int maximumLineCount = 1_000_000;

    public static IReadOnlyList<WindevCodeBlock> Read(
        ReadOnlySpan<byte> data,
        string rootName,
        WindevOpenOptions options)
    {
        List<WindevString> owners = FindOwners(data, options.MaxStringBytes);
        List<WindevCodeBlock> blocks = new List<WindevCodeBlock>();

        for (int eventOffset = 0; eventOffset <= data.Length - 22; eventOffset++)
        {
            if (data[eventOffset] != 0x10)
            {
                continue;
            }

            uint bodyLength = Binary.U32(data, eventOffset + 1);
            uint lineCount = Binary.U32(data, eventOffset + 5);

            if (bodyLength < 17 || bodyLength > data.Length - eventOffset - 5 ||
                lineCount == 0 || lineCount > maximumLineCount)
            {
                continue;
            }

            int eventEnd = eventOffset + 5 + (int)bodyLength;
            List<WindevCodeLine>? lines = ReadLines(
                data[..eventEnd], eventOffset + eventHeaderLength, lineCount, options.MaxStringBytes);

            if (lines is null)
            {
                continue;
            }

            string ownerName = rootName;

            foreach (WindevString owner in owners)
            {
                if (owner.Offset >= eventOffset)
                {
                    break;
                }

                ownerName = owner.Value;
            }

            blocks.Add(new WindevCodeBlock(
                ownerName,
                Binary.U16(data, eventOffset + 9),
                Binary.U16(data, eventOffset + 11),
                eventOffset,
                (int)bodyLength + 5,
                lines.AsReadOnly()));

            eventOffset = eventEnd - 1;
        }

        return blocks.AsReadOnly();
    }

    private static List<WindevString> FindOwners(ReadOnlySpan<byte> data, int maxStringBytes)
    {
        List<WindevString> owners = new List<WindevString>();

        for (int offset = 0; offset <= data.Length - 9; offset++)
        {
            uint nameLength = Binary.U32(data, offset);

            if (nameLength == 0 || nameLength > maxStringBytes || nameLength > data.Length - offset - 9)
            {
                continue;
            }

            int groupOffset = offset + 4 + (int)nameLength;

            if (data[groupOffset] != 0x0a)
            {
                continue;
            }

            uint groupLength = Binary.U32(data, groupOffset + 1);

            if (groupLength > data.Length - groupOffset - 5)
            {
                continue;
            }

            string name = BinaryText.DecodeCp1252(data.Slice(offset + 4, (int)nameLength));

            if (IsIdentifier(name))
            {
                owners.Add(new WindevString(offset, 4 + (int)nameLength, name));
            }
        }

        return owners;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static List<WindevCodeLine>? ReadLines(
        ReadOnlySpan<byte> data,
        int cursor,
        uint lineCount,
        int maxStringBytes)
    {
        List<WindevCodeLine> lines = new List<WindevCodeLine>();

        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            int lineOffset = cursor;

            if (cursor >= data.Length || data[cursor++] != 0)
            {
                return null;
            }

            if (!TryReadText(data, cursor, maxStringBytes, out WindevString source))
            {
                return null;
            }

            cursor += source.ByteLength;

            if (!TryReadText(data, cursor, maxStringBytes, out WindevString author))
            {
                return null;
            }

            cursor += author.ByteLength;

            if (cursor > data.Length - 4)
            {
                return null;
            }

            uint timestamp = Binary.U32(data, cursor);

            cursor += 4;
            lines.Add(new WindevCodeLine(lineOffset, source.Value, author.Value, timestamp));
        }

        return lines;
    }

    private static bool TryReadText(
        ReadOnlySpan<byte> data,
        int offset,
        int maxStringBytes,
        out WindevString result)
    {
        result = null!;

        if (offset < 0 || offset > data.Length - 4)
        {
            return false;
        }

        uint length = Binary.U32(data, offset);

        if (length > maxStringBytes || length > data.Length - offset - 4)
        {
            return false;
        }

        string text = BinaryText.DecodeCp1252(data.Slice(offset + 4, (int)length));
        result = new WindevString(offset, (int)length + 4, text);

        return true;
    }
}
