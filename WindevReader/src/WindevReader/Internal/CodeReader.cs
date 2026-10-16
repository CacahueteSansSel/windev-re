namespace WindevReader.Internal;
internal static class CodeReader
{
    private const int eventHeaderLength = 21;

    private const int lineMetadataLength = 12;

    private const int maximumLineCount = 1_000_000;

    public static IReadOnlyList<WindevCodeBlock> Read(ReadOnlySpan<byte> data, string rootName, WindevOpenOptions options, ushort version)
    {
        if (version == 15)
        {
            return LegacyCodeReader.Read(data, rootName, options);
        }

        byte ownerTag = version is 24 or 25 ? (byte)0x0b : (byte)0x0d;
        List<WindevString> owners = FindOwners(data, options.MaxStringBytes, ownerTag);
        List<WindevCodeBlock> blocks = new List<WindevCodeBlock>();

        for (int eventOffset = 0; eventOffset <= data.Length - 22; eventOffset++)
        {
            bool supportedTag = version is 24 or 25 ? data[eventOffset] == 0x14 : data[eventOffset] is 0x15 or 0x16;

            if (!supportedTag)
            {
                continue;
            }

            uint bodyLength = Binary.U32(data, eventOffset + 1);
            uint lineCount = Binary.U32(data, eventOffset + 5);

            if (bodyLength < 17 || bodyLength > data.Length - eventOffset - 5 || lineCount > maximumLineCount)
            {
                continue;
            }

            int eventEnd = eventOffset + 5 + (int)bodyLength;
            int lineOffset = eventOffset + eventHeaderLength;
            List<WindevCodeLine>? lines = ReadLines(data[..eventEnd], lineOffset, lineCount, options.MaxStringBytes);

            if (lines is null || lines.Count == 0)
            {
                continue;
            }

            string ownerName = FindOwnerName(owners, eventOffset, rootName);
            ushort eventId = Binary.U16(data, eventOffset + 9);
            ushort languageId = Binary.U16(data, eventOffset + 11);

            blocks.Add(new WindevCodeBlock(ownerName, eventId, languageId, eventOffset, (int)bodyLength + 5, lines.AsReadOnly()));

            eventOffset = eventEnd - 1;
        }

        return blocks.AsReadOnly();
    }

    private static List<WindevString> FindOwners(ReadOnlySpan<byte> data, int maxStringBytes, byte ownerTag)
    {
        List<WindevString> owners = new List<WindevString>();

        foreach (WindevString candidate in BinaryText.FindStrings(data, maxStringBytes))
        {
            int stringEnd = candidate.Offset + candidate.ByteLength;

            if (stringEnd + 5 <= data.Length && data[stringEnd] == ownerTag)
            {
                owners.Add(candidate);
            }
        }

        return owners;
    }

    private static string FindOwnerName(IReadOnlyList<WindevString> owners, int eventOffset, string rootName)
    {
        string ownerName = rootName;

        foreach (WindevString candidate in owners)
        {
            if (candidate.Offset >= eventOffset)
            {
                break;
            }

            ownerName = candidate.Value;
        }

        return ownerName;
    }

    private static List<WindevCodeLine>? ReadLines(ReadOnlySpan<byte> data, int cursor, uint lineCount, int maxStringBytes)
    {
        List<WindevCodeLine> lines = new List<WindevCodeLine>();

        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            int lineOffset = cursor;

            if (cursor >= data.Length || data[cursor++] != 3)
            {
                return null;
            }

            string text;

            if (cursor < data.Length && data[cursor] == 0)
            {
                text = string.Empty;

                cursor++;
            }
            else if (BinaryText.TryString(data, cursor, maxStringBytes, out WindevString source))
            {
                text = source.Value;

                cursor += source.ByteLength;
            }
            else
            {
                return null;
            }

            if (!BinaryText.TryString(data, cursor, maxStringBytes, out WindevString author))
            {
                return null;
            }

            cursor += author.ByteLength;

            if (cursor > data.Length - lineMetadataLength)
            {
                return null;
            }

            uint timestamp = Binary.U32(data, cursor);

            cursor += lineMetadataLength;

            lines.Add(new WindevCodeLine(lineOffset, text, author.Value, timestamp));
        }

        return lines;
    }
}
