namespace WindevReader.Internal;
/// <summary>Independent implementation of the legacy PCS byte transformation used by WDD 0x061D.</summary>
internal static class PcsLegacyCipher
{
    private static readonly byte[] analysisKey = DeriveKey([15, 80, 90, 110, 56, 23, 19, 8, 13, 21]);

    // Static archive-format parameter recovered from the supplied runtime, not an application password.
    private static readonly byte[] libraryKey = DeriveKey("&K_G@HjO:#pl\\i)r"u8.ToArray());

    internal static byte[] DecodeLibrary(ReadOnlySpan<byte> source)
    {
        byte[] result = source.ToArray();

        Decrypt(result, libraryKey);

        return result;
    }

    private static byte[] DeriveKey(byte[] password)
    {
        byte[] key = Convert.FromHexString("8805090245001233010403091C226566");
        byte[] temporaryBuffer = (byte[])password.Clone();

        temporaryBuffer[0] %= 29;

        for (int forwardIndex = 1; forwardIndex < temporaryBuffer.Length; forwardIndex++)
        {
            temporaryBuffer[forwardIndex] ^= temporaryBuffer[forwardIndex - 1];
        }

        Encrypt(temporaryBuffer, Convert.FromHexString("091C0A000D07FD00652020030D01550A"));

        int length = Math.Max(16, temporaryBuffer.Length);

        for (int keyIndex = 0; keyIndex < length; keyIndex++)
        {
            key[keyIndex & 15] = unchecked((byte)((temporaryBuffer[keyIndex % temporaryBuffer.Length] + key[(keyIndex + 1) & 15]) ^ (length * keyIndex)));
        }

        return key;
    }

    private static void Encrypt(Span<byte> data, ReadOnlySpan<byte> key)
    {
        int length = data.Length;

        data[0] = unchecked((byte)((data[0] + data[length - 1]) ^ key[15]));

        for (int forwardIndex = 1, keyIndex = 0; forwardIndex < length; forwardIndex++, keyIndex = (keyIndex + 1) & 15)
        {
            int backwardIndex = length - 1 - forwardIndex;
            int transformedByte;

            if (forwardIndex == backwardIndex)
            {
                data[forwardIndex] = unchecked((byte)((data[forwardIndex] + data[forwardIndex - 1]) ^ key[15 - keyIndex]));

                transformedByte = unchecked((byte)(data[backwardIndex] + 17));
            }
            else
            {
                data[forwardIndex] = unchecked((byte)((data[forwardIndex] + data[forwardIndex - 1] + data[backwardIndex]) ^ key[15 - keyIndex]));

                transformedByte = unchecked((byte)(data[forwardIndex] + data[backwardIndex]));
            }

            data[backwardIndex] = (byte)(transformedByte ^ key[keyIndex]);
        }
    }

    public static byte[] DecodeAnalysis(ReadOnlySpan<byte> source)
    {
        if (source.Length < 6 || Binary.U16(source, 0) != 0x60 || Binary.U16(source, 2) != 0x061d)
        {
            throw new UnsupportedWindevFormatException("Unsupported WDD schema encoding (expected 0x0060 / 0x061D)");
        }

        byte[] result = source.ToArray();

        Decrypt(result.AsSpan(4), analysisKey);

        return result;
    }

    private static void Decrypt(Span<byte> data, ReadOnlySpan<byte> key)
    {
        int length = data.Length;

        if (length < 2)
        {
            throw new WindevFormatException("Truncated legacy cipher buffer");
        }

        for (int backwardIndex = length - 1, keyIndex = (length - 2) & 15; backwardIndex > 0; backwardIndex--, keyIndex = (keyIndex - 1) & 15)
        {
            int forwardIndex = length - 1 - backwardIndex;

            if (forwardIndex == backwardIndex)
            {
                data[backwardIndex] = unchecked((byte)((((data[backwardIndex] ^ key[keyIndex]) - 17) ^ key[15 - keyIndex]) - data[backwardIndex - 1]));
            }
            else
            {
                data[forwardIndex] = unchecked((byte)((data[forwardIndex] ^ key[keyIndex]) - data[backwardIndex]));

                data[backwardIndex] = unchecked((byte)((data[backwardIndex] ^ key[15 - keyIndex]) - data[backwardIndex - 1] - data[forwardIndex]));
            }
        }

        data[0] = unchecked((byte)((data[0] ^ key[15]) - data[length - 1]));
    }
}
