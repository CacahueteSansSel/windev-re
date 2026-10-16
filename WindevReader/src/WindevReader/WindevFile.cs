using System.Text.Json.Serialization;
using WindevReader.Internal;

namespace WindevReader;
/// <summary>Read-only file access shared by PCS documents, compiled libraries and physical HFSQL files.</summary>
public interface IWindevFile
{
    string FileName { get; }

    string Name { get; }

    [JsonIgnore]
    ReadOnlyMemory<byte> RawData { get; }

    IReadOnlyList<WindevDiagnostic> Diagnostics { get; }
}

public static class WindevFile
{
    /// <summary>Dispatches by PCS type, including numbered WDL patches whose extension is a number.</summary>
    public static IWindevFile Open(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();

        options.Validate();

        using FileStream stream = File.OpenRead(filename);

        if (stream.Length > options.MaxFileBytes)
        {
            throw new WindevFormatException("File exceeds reader limit");
        }

        Span<byte> header = stackalloc byte[20];

        if (stream.ReadAtLeast(header, 20, false) != 20)
        {
            throw new WindevFormatException("Truncated PCS header");
        }

        if (!header[..4].SequenceEqual("PCS\0"u8))
        {
            throw new WindevFormatException("Not a PCS file", 0);
        }

        return Binary.U32(header, 8) switch
        {
            0xD0001 => WdlWindevLibrary.Open(filename, options),
            0x70001 => FicHfsqlDataFile.Open(filename, options),
            0x70002 => NdxHfsqlIndexFile.Open(filename, options),
            0x70003 => MmoHfsqlMemoFile.Open(filename, options),
            0x1001 => WdpWindevProject.Open(filename, options),
            2 => WdwWindevWindow.Open(filename, options),
            7 => WdgWindevGlobalProcedures.Open(filename, options),
            0x70004 => WddWindevAnalysis.Open(filename, options),
            3 => WdeWindevReport.Open(filename, options),
            0x80002 => WdaWindevAnalysisSource.Open(filename, options),
            _ => throw new UnsupportedWindevFormatException("Unsupported PCS file type", 8)
        };
    }
}

internal static class FileInput
{
    internal static byte[] Read(string filename, uint expectedType, WindevOpenOptions options, int minimum)
    {
        options.Validate();

        using FileStream stream = File.OpenRead(filename);

        if (stream.Length > options.MaxFileBytes)
        {
            throw new WindevFormatException("File exceeds reader limit");
        }

        if (stream.Length < minimum)
        {
            throw new WindevFormatException("Truncated file");
        }

        byte[] data = new byte[(int)stream.Length];

        stream.ReadExactly(data);

        if (!data.AsSpan(0, 4).SequenceEqual("PCS\0"u8))
        {
            throw new WindevFormatException("Not a PCS file", 0);
        }

        if (Binary.U32(data, 4) != 20)
        {
            throw new UnsupportedWindevFormatException("Unsupported PCS header size/revision", 4);
        }

        if (Binary.U32(data, 8) != expectedType)
        {
            throw new UnsupportedWindevFormatException("Wrong PCS file type", 8);
        }

        bool knownVersion = Binary.U16(data, 14) == 31 || (expectedType is 0x70001 or 0x70002 && Binary.U16(data, 14) == 28) || (expectedType is 0x70001 or 0x70002 or 0x70003 && Binary.U16(data, 14) is 15 or 24 or 25 or 29);

        if (!knownVersion && !options.AllowUntestedVersions)
        {
            throw new UnsupportedWindevFormatException(
                "Unsupported version for this file family (HFSQL FIC/NDX: 15, 24, 25, 28, 29 and 31; MMO: 15, 24, 25, 29 and 31; WDL: 31)",
                14);
        }

        return data;
    }
}
