using System.Buffers.Binary;
using WindevReader;
using WindevReader.Internal;

internal static class SyntheticKeyTests
{
    internal static void Run()
    {
        VerifyPointerDiagnostics();
        VerifyMetadataDump();

        byte[] data = new byte[256];
        WindevDataField[] fields =
        [
            CreateField(1, "First", 0, false),
            CreateField(2, "Second", 64, false),
            CreateField(3, "Composite", 128, true)
        ];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(87), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(89), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(174), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(200), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(205), 1);
        data[207] = 0x80;

        int total = 0;
        IReadOnlyList<WindevDataField> resolved = SchemaKeyReader.ResolveComponents(data, fields, new WindevOpenOptions(), ref total);

        Require(resolved[2].Key!.ComponentsDecoded && resolved[2].KeyComponents.Count == 2 && total == 2);
        Require(resolved[2].KeyComponents[1].FieldName == "Second" && resolved[2].KeyComponents[1].RawOptions == 0x80);

        foreach (int offset in new int[] { 87, 89 })
        {
            byte[] reordered = (byte[])data.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(reordered.AsSpan(offset), 0);
            total = 0;
            IReadOnlyList<WindevDataField> unresolved = SchemaKeyReader.ResolveComponents(reordered, fields, new WindevOpenOptions(), ref total);

            Require(!unresolved[2].Key!.ComponentsDecoded && unresolved[2].KeyComponents.Count == 0);
            Require(unresolved[2].Key!.ComponentsOffset == 200 && unresolved[2].KeyKind == WindevKeyKind.Duplicates && total == 2);
            Require(unresolved[0] == fields[0] && unresolved[1] == fields[1]);

            ExpectRejected(reordered, fields, new WindevOpenOptions { MaxIndexEntries = 1 });
            BinaryPrimitives.WriteUInt16LittleEndian(reordered.AsSpan(202), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(reordered.AsSpan(205), 99);
            ExpectRejected(reordered, fields, new WindevOpenOptions());
        }

        foreach (ushort ordinal in new ushort[] { 2, 99 })
        {
            byte[] malformed = (byte[])data.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(202), ordinal);
            ExpectRejected(malformed, fields, new WindevOpenOptions());
        }

        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(174), 255);
        ExpectRejected(data, fields, new WindevOpenOptions());
        Console.WriteLine("Synthetic composite key tests passed.");
    }

    private static WindevDataField CreateField(uint id, string name, int suffixOffset, bool composite)
    {
        return new WindevDataField(id, name, "", "", "", composite ? (ushort)28 : (ushort)2, 0, 0, 0, 0)
        {
            RawCompositeMarker = composite ? (byte)1 : (byte)0,
            Key = new WindevFieldKey(suffixOffset + 27, 2, null, false, Array.Empty<WindevKeyComponent>())
        };
    }

    private static void VerifyMetadataDump()
    {
        string originalDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "windev-dump-" + Guid.NewGuid().ToString("N"));
        byte[] data = new byte[128];

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            VerifyExtendedFieldDirectory();
            File.Delete("except.bin");
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(88), 6);
            Require(SchemaReader.ReadFieldDirectoryPointer(data, 10, 0, 6, 31) == 0);
            Require(!File.Exists("except.bin"));
            data.AsSpan(10, 82).Fill(0xAB);

            try
            {
                SchemaReader.ReadFieldDirectoryPointer(data, 10, 0, 6, 31);
                throw new Exception("Invalid metadata pointer was accepted.");
            }
            catch (WindevFormatException exception)
            {
                Require(exception.Offset == 80);
                Require((string?)exception.Data["MetadataDumpPath"] == Path.Combine(temporaryDirectory, "except.bin"));
                Require(File.ReadAllBytes("except.bin").AsSpan().SequenceEqual(data.AsSpan(10, 82)));
            }

            File.Delete("except.bin");
            Directory.CreateDirectory("except.bin");

            try
            {
                SchemaReader.ReadFieldDirectoryPointer(data, 10, 0, 6, 31);
                throw new Exception("Invalid metadata pointer was accepted after dump failure.");
            }
            catch (WindevFormatException exception)
            {
                Require(exception.Offset == 80 && exception.Data.Contains("MetadataDumpError"));
            }

            Directory.Delete("except.bin");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            File.Delete(Path.Combine(temporaryDirectory, "except.bin"));
            Directory.Delete(temporaryDirectory);
        }
    }

    private static void VerifyExtendedFieldDirectory()
    {
        byte[] data = new byte[512];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32 + 70), 0xCE600000AAB);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32 + 78), 200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32 + 86), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(204), 300);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(216), 350);
        data[300] = 0x20;
        data[350] = 0x20;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(301), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(351), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(309), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(359), 42);

        Require(SchemaReader.ReadFieldDirectoryPointer(data, 32, 0, 2, 31) == 200);
        Require(!File.Exists("except.bin"));

        foreach (int mutationOffset in new int[] { 118, 110, 204, 300, 359, 351 })
        {
            byte[] malformed = (byte[])data.Clone();
            malformed[mutationOffset] = 0xFF;

            // A different field ID is valid; duplicate IDs must be rejected instead.
            if (mutationOffset == 351)
            {
                malformed[mutationOffset] = 1;
            }

            try
            {
                SchemaReader.ReadFieldDirectoryPointer(malformed, 32, 0, 2, 31);
                throw new Exception("Invalid extended field directory was accepted.");
            }
            catch (WindevFormatException exception)
            {
                Require(exception.Offset == 102);
            }
        }
    }

    private static void VerifyPointerDiagnostics()
    {
        Require(Binary.Offset(0, 256) == 0 && Binary.Offset(256, 256) == 256);

        foreach (ulong pointer in new ulong[] { 257, ulong.MaxValue })
        {
            try
            {
                Binary.Offset(pointer, 256, 70, "field directory");
            }
            catch (WindevFormatException exception)
            {
                Require(exception.Offset == 70);
                Require(exception.Message.Contains("field directory") &&
                    exception.Message.Contains($"target 0x{pointer:X}") &&
                    exception.Message.Contains("buffer length 0x100"));

                continue;
            }

            throw new Exception("Invalid pointer was accepted.");
        }
    }

    private static void ExpectRejected(byte[] data, WindevDataField[] fields, WindevOpenOptions options)
    {
        int total = 0;

        try
        {
            SchemaKeyReader.ResolveComponents(data, fields, options, ref total);
        }
        catch (WindevFormatException)
        {
            return;
        }

        throw new Exception("Malformed or over-budget composite key was accepted.");
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new Exception("Synthetic composite key assertion failed.");
        }
    }
}
