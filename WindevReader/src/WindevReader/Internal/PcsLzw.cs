namespace WindevReader.Internal;

/// <summary>PCS LSB-first LZW: clear=0, literals=1..256, EOF=257, codes grow from 9 to 16 bits.</summary>
internal static class PcsLzw
{
    public static byte[] Decode(ReadOnlySpan<byte> source, int expectedLength, int maxBytes)
    {
        if (expectedLength < 0 || expectedLength > maxBytes)
            throw new WindevFormatException("Decoded LZW length exceeds reader limit");
        byte[] output = new byte[expectedLength];
        var prefix = new ushort[65536]; var suffix = new byte[65536]; var stack = new byte[65536];
        int bit = 0, width = 9, next = 257, previous = -1, count = 0;
        byte first = 0;
        while (true)
        {
            if ((long)bit + width > (long)source.Length * 8)
                throw new WindevFormatException("LZW stream has no end code");
            int code = 0;
            for (int j = 0; j < width; j++) code |= ((source[(bit + j) / 8] >> ((bit + j) % 8)) & 1) << j;
            bit += width;
            if (code == 0) { width = 9; next = 257; previous = -1; continue; }
            if (code == 257)
            {
                if (count != expectedLength || (bit + 7) / 8 != source.Length)
                    throw new WindevFormatException("LZW declared length or encoded length mismatch");
                return output;
            }
            int current = code, top = 0;
            if (code == next && previous >= 0) { stack[top++] = first; current = previous; }
            else if (code >= next || code > 65535) throw new WindevFormatException("Invalid LZW dictionary code");
            while (current >= 258)
            {
                if (top >= stack.Length - 1 || current >= next)
                    throw new WindevFormatException("Invalid LZW dictionary chain");
                stack[top++] = suffix[current]; current = prefix[current];
            }
            if (current is < 1 or > 256) throw new WindevFormatException("Invalid LZW literal");
            first = (byte)(current - 1); stack[top++] = first;
            if (top > output.Length - count) throw new WindevFormatException("LZW output exceeds declared size");
            while (top > 0) output[count++] = stack[--top];
            if (previous >= 0 && next < 65536) { prefix[next] = (ushort)previous; suffix[next] = first; }
            if (next < 65536) next++;
            if (next >= 1 << width && width < 16) width++;
            previous = code;
        }
    }
}
