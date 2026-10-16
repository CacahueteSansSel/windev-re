namespace WindevReader.Internal;
/// <summary>Reads the PCS LZW variant: least-significant bits first, 9 to 16-bit codes.</summary>
internal static class PcsLzw
{
    private const int clearCode = 0;

    private const int endCode = 257;

    private const int firstDictionaryCode = 258;

    private const int dictionaryCapacity = 65536;

    public static byte[] Decode(ReadOnlySpan<byte> source, int expectedLength, int maxBytes)
    {
        if (expectedLength < 0 || expectedLength > maxBytes)
        {
            throw new WindevFormatException("Decoded LZW length exceeds reader limit");
        }

        byte[] output = new byte[expectedLength];
        ushort[] prefixes = new ushort[dictionaryCapacity];
        byte[] suffixes = new byte[dictionaryCapacity];
        byte[] sequenceStack = new byte[dictionaryCapacity];
        int bitOffset = 0;
        int codeWidth = 9;
        int nextCode = endCode;
        int previousCode = -1;
        int outputLength = 0;
        byte firstByte = 0;

        while (true)
        {
            int code = ReadCode(source, ref bitOffset, codeWidth);

            if (code == clearCode)
            {
                codeWidth = 9;

                nextCode = endCode;

                previousCode = -1;

                continue;
            }

            if (code == endCode)
            {
                ValidateEnd(source.Length, bitOffset, expectedLength, outputLength);

                return output;
            }

            int currentCode = code;
            int stackLength = 0;

            // A code may refer to the sequence that is being added to the dictionary.
            if (code == nextCode && previousCode >= 0)
            {
                sequenceStack[stackLength++] = firstByte;

                currentCode = previousCode;
            }
            else if (code >= nextCode || code >= dictionaryCapacity)
            {
                throw new WindevFormatException("Invalid LZW dictionary code");
            }

            while (currentCode >= firstDictionaryCode)
            {
                if (stackLength >= sequenceStack.Length - 1 || currentCode >= nextCode)
                {
                    throw new WindevFormatException("Invalid LZW dictionary chain");
                }

                sequenceStack[stackLength++] = suffixes[currentCode];

                currentCode = prefixes[currentCode];
            }

            if (currentCode is < 1 or > 256)
            {
                throw new WindevFormatException("Invalid LZW literal");
            }

            firstByte = (byte)(currentCode - 1);

            sequenceStack[stackLength++] = firstByte;

            if (stackLength > output.Length - outputLength)
            {
                throw new WindevFormatException("LZW output exceeds declared size");
            }

            while (stackLength > 0)
            {
                output[outputLength++] = sequenceStack[--stackLength];
            }

            if (previousCode >= 0 && nextCode < dictionaryCapacity)
            {
                prefixes[nextCode] = (ushort)previousCode;

                suffixes[nextCode] = firstByte;
            }

            if (nextCode < dictionaryCapacity)
            {
                nextCode++;
            }

            if (nextCode >= 1 << codeWidth && codeWidth < 16)
            {
                codeWidth++;
            }

            previousCode = code;
        }
    }

    private static int ReadCode(ReadOnlySpan<byte> source, ref int bitOffset, int codeWidth)
    {
        if ((long)bitOffset + codeWidth > (long)source.Length * 8)
        {
            throw new WindevFormatException("LZW stream has no end code");
        }

        int code = 0;

        for (int bitIndex = 0; bitIndex < codeWidth; bitIndex++)
        {
            int sourceBitOffset = bitOffset + bitIndex;
            int sourceBit = (source[sourceBitOffset / 8] >> (sourceBitOffset % 8)) & 1;

            code |= sourceBit << bitIndex;
        }

        bitOffset += codeWidth;

        return code;
    }

    private static void ValidateEnd(int sourceLength, int bitOffset, int expectedLength, int outputLength)
    {
        if (outputLength != expectedLength || (bitOffset + 7) / 8 != sourceLength)
        {
            throw new WindevFormatException("LZW declared length or encoded length mismatch");
        }
    }
}
