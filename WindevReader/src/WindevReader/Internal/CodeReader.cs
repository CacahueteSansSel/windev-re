namespace WindevReader.Internal;
internal static class CodeReader
{
    public static IReadOnlyList<WindevCodeBlock> Read(ReadOnlySpan<byte> data, string rootName, WindevOpenOptions options)
    {
        var owners = new List<WindevString>();
        foreach (var s in BinaryText.FindStrings(data, options.MaxStringBytes))
            if (s.Offset + s.ByteLength + 5 <= data.Length && data[s.Offset + s.ByteLength] == 0x0d)
                owners.Add(s);
        var blocks = new List<WindevCodeBlock>();
        for (int p = 0; p <= data.Length - 22; p++)
        {
            if (data[p] is not (0x15 or 0x16)) continue;
            uint length = Binary.U32(data, p + 1), lineCount = Binary.U32(data, p + 5);
            if (length < 17 || length > data.Length - p - 5 || lineCount > 1000000) continue;
            int end = p + 5 + (int)length, cursor = p + 21;
            var lines = new List<WindevCodeLine>(); bool valid = true;
            for (int j = 0; j < lineCount; j++)
            {
                int start = cursor;
                if (cursor >= end || data[cursor++] != 3) { valid = false; break; }
                string text;
                if (cursor < end && data[cursor] == 0) { text = ""; cursor++; }
                else if (BinaryText.TryString(data[..end], cursor, options.MaxStringBytes, out var source))
                { text = source.Value; cursor += source.ByteLength; }
                else { valid = false; break; }
                if (!BinaryText.TryString(data[..end], cursor, options.MaxStringBytes, out var author))
                { valid = false; break; }
                cursor += author.ByteLength;
                if (cursor > end - 12) { valid = false; break; }
                uint timestamp = Binary.U32(data, cursor); cursor += 12;
                lines.Add(new(start, text, author.Value, timestamp));
            }
            if (!valid || lineCount == 0) continue;
            string owner = rootName;
            foreach (var candidate in owners) { if (candidate.Offset >= p) break; owner = candidate.Value; }
            blocks.Add(new(owner, Binary.U16(data, p + 9), Binary.U16(data, p + 11), p, (int)length + 5, lines.AsReadOnly()));
            p = end - 1;
        }
        return blocks.AsReadOnly();
    }
}
