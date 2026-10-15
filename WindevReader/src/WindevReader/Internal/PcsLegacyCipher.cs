namespace WindevReader.Internal;

/// <summary>Independent implementation of the legacy PCS byte transformation used by WDD 0x061D.</summary>
internal static class PcsLegacyCipher
{
    private static readonly byte[] AnalysisKey = DeriveKey([15, 80, 90, 110, 56, 23, 19, 8, 13, 21]);
    // Static archive-format parameter recovered from the supplied runtime, not an application password.
    private static readonly byte[] LibraryKey = DeriveKey("&K_G@HjO:#pl\\i)r"u8.ToArray());
    internal static byte[] DecodeLibrary(ReadOnlySpan<byte> source)
    {
        byte[] result = source.ToArray(); Decrypt(result, LibraryKey); return result;
    }
    private static byte[] DeriveKey(byte[] password)
    {
        byte[] key = Convert.FromHexString("8805090245001233010403091C226566");
        var temp = (byte[])password.Clone(); temp[0] %= 29;
        for (int i = 1; i < temp.Length; i++) temp[i] ^= temp[i - 1];
        Encrypt(temp, Convert.FromHexString("091C0A000D07FD00652020030D01550A"));
        int n = Math.Max(16, temp.Length);
        for (int i = 0; i < n; i++)
            key[i & 15] = unchecked((byte)((temp[i % temp.Length] + key[(i + 1) & 15]) ^ (n * i)));
        return key;
    }
    private static void Encrypt(Span<byte> data, ReadOnlySpan<byte> key)
    {
        int n = data.Length;
        data[0] = unchecked((byte)((data[0] + data[n - 1]) ^ key[15]));
        for (int i = 1, k = 0; i < n; i++, k = (k + 1) & 15)
        {
            int j = n - 1 - i, a;
            if (i == j)
            {
                data[i] = unchecked((byte)((data[i] + data[i - 1]) ^ key[15 - k]));
                a = unchecked((byte)(data[j] + 17));
            }
            else
            {
                data[i] = unchecked((byte)((data[i] + data[i - 1] + data[j]) ^ key[15 - k]));
                a = unchecked((byte)(data[i] + data[j]));
            }
            data[j] = (byte)(a ^ key[k]);
        }
    }
    public static byte[] DecodeAnalysis(ReadOnlySpan<byte> source)
    {
        if (source.Length < 6 || Binary.U16(source, 0) != 0x60 || Binary.U16(source, 2) != 0x061d)
            throw new UnsupportedWindevFormatException("Unsupported WDD schema encoding (expected 0x0060 / 0x061D)");
        byte[] result = source.ToArray(); Decrypt(result.AsSpan(4), AnalysisKey); return result;
    }
    private static void Decrypt(Span<byte> data, ReadOnlySpan<byte> key)
    {
        int n = data.Length;
        if (n < 2) throw new WindevFormatException("Truncated legacy cipher buffer");
        for (int j = n - 1, k = (n - 2) & 15; j > 0; j--, k = (k - 1) & 15)
        {
            int i = n - 1 - j;
            if (i == j)
                data[j] = unchecked((byte)((((data[j] ^ key[k]) - 17) ^ key[15 - k]) - data[j - 1]));
            else
            {
                data[i] = unchecked((byte)((data[i] ^ key[k]) - data[j]));
                data[j] = unchecked((byte)((data[j] ^ key[15 - k]) - data[j - 1] - data[i]));
            }
        }
        data[0] = unchecked((byte)((data[0] ^ key[15]) - data[n - 1]));
    }
}
