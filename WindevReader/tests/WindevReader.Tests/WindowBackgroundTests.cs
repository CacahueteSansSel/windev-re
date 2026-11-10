using System.Buffers.Binary;
using System.Text;
using WindevReader;

internal static class WindowBackgroundTests
{
    internal static void Run()
    {
        foreach (string name in new string[] { "Window", "SyntheticWindowLongName", "Fenêtre" })
        {
            foreach (uint raw in new uint[] { 0, 0xFF, 0xFF00, 0xFF0000, 0xF5F5F5, 0xF6F6F6, 0x563412, 0xFFFFFF, 0x80000005, uint.MaxValue })
            {
                byte[] data = CreateRoot(name, raw);
                int expectedOffset = data.Length - 4;
                (uint? actualRaw, int? offset, WindevRgbColor? color) = WindowBackgroundReader.Read(data, 29, name, 10000);

                Check(actualRaw == raw && offset == expectedOffset, "Raw value and offset must follow synthetic metadata.");

                if (raw <= 0xFFFFFF)
                {
                    Check(color == new WindevRgbColor((byte)raw, (byte)(raw >> 8), (byte)(raw >> 16)), "RGB channel order must match storage.");
                }
                else
                {
                    Check(color == null, "Special values must remain uninterpreted.");
                }
            }
        }

        byte[] valid = CreateRoot("Window", 0x563412);
        Check(WindowBackgroundReader.Read(valid, 29, "Window", 10000).Color?.Hex == "#123456", "Hex must use RGB order.");

        for (int length = 0; length < valid.Length; length++)
        {
            Check(WindowBackgroundReader.Read(valid.AsSpan(0, length), 29, "Window", 10000).RawValue == null, "Truncated data must not expose a color.");
        }

        foreach (ushort version in new ushort[] { 15, 24, 25, 28, 31, 99 })
        {
            Check(WindowBackgroundReader.Read(valid, version, "Window", 10000).RawValue == null, "Unverified versions must remain unsupported.");
        }

        Check(WindowBackgroundReader.Read(valid, 29, "Other", 10000).RawValue == null, "Root identity must match.");
        Check(WindowBackgroundReader.Read(valid, 29, "Window", 1).RawValue == null, "String budget must be respected.");

        foreach (int offset in new int[] { 4, 8, 12, 22, 23 })
        {
            byte[] malformed = (byte[])valid.Clone();
            malformed[offset] ^= 0xFF;
            Check(WindowBackgroundReader.Read(malformed, 29, "Window", 10000).RawValue == null, "Invalid descriptors and framed strings must be rejected.");
        }

        Console.WriteLine("Synthetic window background tests passed; no specimen files read or written.");
    }

    private static byte[] CreateRoot(string name, uint raw)
    {
        byte[] text = Encoding.UTF8.GetBytes(name);
        int frameLength = 13 + text.Length;
        int colorOffset = 8 + frameLength + 81;
        byte[] data = new byte[colorOffset + 4];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 607);
        new byte[] { 0xFF, 0xCE, 0x88, 0xF1 }.CopyTo(data, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)frameLength);
        text.CopyTo(data, 16);
        new byte[] { 0xFE, 0xED, 0x98, 0xE1 }.CopyTo(data, 17 + text.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(colorOffset), raw);

        return data;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
