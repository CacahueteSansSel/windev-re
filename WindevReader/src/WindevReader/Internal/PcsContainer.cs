using System.Text;

namespace WindevReader.Internal;
internal sealed record PcsContainer(
    byte[] RawData,
    WindevHeader Header,
    IReadOnlyList<WindevSection> Sections,
    List<WindevDiagnostic> Diagnostics)
{
    private const int directoryOffset = 85;
    private const int directoryEntryLength = 5;
    private const int compressionHeaderLength = 11;
    public static PcsContainer Read(string filename, WindevOpenOptions options)
    {
        options.Validate();

        using FileStream stream = File.OpenRead(filename);

        if (stream.Length > options.MaxFileBytes)
        {
            throw new WindevFormatException("File exceeds reader limit");
        }

        byte[] data = new byte[(int)stream.Length];

        stream.ReadExactly(data);

        return Parse(data, options);
    }

    public static PcsContainer Parse(byte[] data, WindevOpenOptions options)
    {
        options.Validate();

        ValidateHeader(data);

        ushort version = Binary.U16(data, 14);
        uint documentType = Binary.U32(data, 8);
        bool knownVersion = version is 28 or 31 ||
            (version is 15 or 24 or 25 or 29 && documentType is 0x1001 or 2 or 0x70004) ||
            (version == 15 && documentType == 0x80002);

        if (!knownVersion && !options.AllowUntestedVersions)
        {
            throw new UnsupportedWindevFormatException($"WINDEV {version} was not validated; opt in with AllowUntestedVersions", 14);
        }

        int sectionCount = Binary.Count(Binary.U32(data, 81), 256, 81);
        int payloadOffset = ReadPayloadOffset(data, sectionCount);
        List<WindevDiagnostic> diagnostics = new List<WindevDiagnostic>();

        if (!knownVersion)
        {
            diagnostics.Add(new WindevDiagnostic("UntestedVersion", DiagnosticSeverity.Warning, $"WINDEV {version} is untested."));
        }

        List<WindevSection> sections = new List<WindevSection>();
        int sectionOffset = payloadOffset;
        int totalDecodedBytes = 0;
        int decodedBlockCount = 0;

        for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            int entryOffset = directoryOffset + sectionIndex * directoryEntryLength;
            byte tag = data[entryOffset];
            int sectionLength = (int)Binary.U32(data, entryOffset + 1);
            ReadOnlyMemory<byte> sectionData = data.AsMemory(sectionOffset, sectionLength);
            List<WindevDecodedBlock> blocks;

            if ((documentType & 0xffff) == 4 && sectionIndex == 1 && sectionLength != 0)
            {
                WindevDecodedBlock schemaBlock = DecodeSchema(sectionData, options, ref totalDecodedBytes, ref decodedBlockCount);

                blocks = new List<WindevDecodedBlock>
                {
                    schemaBlock
                };
            }
            else
            {
                bool legacyPropertyText = version == 15 && documentType == 2 && sectionIndex == 1;

                blocks = FindCompressedBlocks(
                    sectionData, options, ref totalDecodedBytes, ref decodedBlockCount, legacyPropertyText);

                if (sectionIndex == 2 && tag == 2 && sectionLength > 0 && blocks.All(block => block.Offset != 4))
                {
                    throw new WindevFormatException("Code section could not be decoded", sectionOffset + 4);
                }
            }

            sections.Add(new WindevSection(sectionIndex, tag, sectionOffset, sectionData, blocks.AsReadOnly(), options));

            sectionOffset += sectionLength;
        }

        WindevHeader header = new WindevHeader(
            20,
            documentType,
            version,
            Binary.U16(data, 12),
            Encoding.ASCII.GetString(data, 70, 10),
            payloadOffset,
            sectionCount);

        return new PcsContainer(data, header, sections.AsReadOnly(), diagnostics);
    }

    private static void ValidateHeader(ReadOnlySpan<byte> data)
    {
        Binary.Require(data, 0, directoryOffset);

        if (!data[..4].SequenceEqual("PCS\0"u8))
        {
            throw new WindevFormatException("Not a PCS document", 0);
        }

        if (Binary.U32(data, 4) != 20)
        {
            throw new UnsupportedWindevFormatException("Unsupported PCS container revision", 4);
        }
    }

    private static int ReadPayloadOffset(ReadOnlySpan<byte> data, int sectionCount)
    {
        if (sectionCount == 0)
        {
            throw new WindevFormatException("PCS section directory is empty", 81);
        }

        int directoryLength = sectionCount * directoryEntryLength;

        Binary.Require(data, directoryOffset, directoryLength);

        int payloadOffset = Binary.Offset(32UL + Binary.U32(data, 32), data.Length);

        if (payloadOffset < directoryOffset + directoryLength)
        {
            throw new WindevFormatException("PCS payload overlaps its section directory", 32);
        }

        long expectedEnd = payloadOffset;

        for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            expectedEnd += Binary.U32(data, directoryOffset + 1 + sectionIndex * directoryEntryLength);
        }

        if (expectedEnd != data.Length)
        {
            throw new WindevFormatException("PCS section lengths do not cover the file exactly", directoryOffset);
        }

        return payloadOffset;
    }

    private static WindevDecodedBlock DecodeSchema(
        ReadOnlyMemory<byte> sectionData,
        WindevOpenOptions options,
        ref int totalDecodedBytes,
        ref int decodedBlockCount)
    {
        byte[] transformedData = PcsLegacyCipher.DecodeAnalysis(sectionData.Span);
        byte[] decodedData = DecodeLzwWrapper(transformedData, 4, options, out int storedLength);
        byte[] schemaData = new byte[decodedData.Length + 4];

        transformedData.AsSpan(0, 4).CopyTo(schemaData);

        decodedData.CopyTo(schemaData, 4);

        CheckBudget(schemaData.Length, options, ref totalDecodedBytes, ref decodedBlockCount);

        return new WindevDecodedBlock(0, storedLength + 4, "PCS-legacy + LZW", schemaData, options);
    }

    private static List<WindevDecodedBlock> FindCompressedBlocks(
        ReadOnlyMemory<byte> sectionData,
        WindevOpenOptions options,
        ref int totalDecodedBytes,
        ref int decodedBlockCount,
        bool legacyText)
    {
        List<WindevDecodedBlock> blocks = new List<WindevDecodedBlock>();

        for (int blockOffset = 0; blockOffset <= sectionData.Length - compressionHeaderLength; blockOffset++)
        {
            ReadOnlySpan<byte> data = sectionData.Span;

            if (data[blockOffset] != 4 || data[blockOffset + 9] != 0 || data[blockOffset + 10] != 0)
            {
                continue;
            }

            uint encodedLength = Binary.U32(data, blockOffset + 1);
            uint decodedLength = Binary.U32(data, blockOffset + 5);

            if (encodedLength < 2 || encodedLength > data.Length - blockOffset - compressionHeaderLength || decodedLength > options.MaxDecodedBlockBytes)
            {
                continue;
            }

            byte[] decodedData;

            try
            {
                decodedData = DecodeLzwWrapper(data, blockOffset, options, out _);
            }
            catch (WindevFormatException)
            {
                // Opaque regions may contain bytes that only resemble a compression envelope.
                continue;
            }

            CheckBudget(decodedData.Length, options, ref totalDecodedBytes, ref decodedBlockCount);

            int storedLength = compressionHeaderLength + (int)encodedLength;

            blocks.Add(new WindevDecodedBlock(blockOffset, storedLength, "PCS-LZW", decodedData, options, legacyText));

            blockOffset += storedLength - 1;
        }

        return blocks;
    }

    public static byte[] DecodeLzwWrapper(ReadOnlySpan<byte> data, int offset, WindevOpenOptions options, out int storedLength)
    {
        Binary.Require(data, offset, compressionHeaderLength);

        if (data[offset] != 4 || data[offset + 9] != 0 || data[offset + 10] != 0)
        {
            throw new UnsupportedWindevFormatException("Unsupported compression envelope", offset);
        }

        int encodedLength = Binary.Count(Binary.U32(data, offset + 1), data.Length, offset + 1);
        int decodedLength = Binary.Count(Binary.U32(data, offset + 5), options.MaxDecodedBlockBytes, offset + 5);

        Binary.Require(data, offset + compressionHeaderLength, encodedLength);

        storedLength = encodedLength + compressionHeaderLength;

        return PcsLzw.Decode(data.Slice(offset + compressionHeaderLength, encodedLength), decodedLength, options.MaxDecodedBlockBytes);
    }

    private static void CheckBudget(int length, WindevOpenOptions options, ref int totalDecodedBytes, ref int decodedBlockCount)
    {
        if (++decodedBlockCount > options.MaxDecodedBlocks || length > options.MaxTotalDecodedBytes - totalDecodedBytes)
        {
            throw new WindevFormatException("Total decompression budget exceeded");
        }

        totalDecodedBytes += length;
    }
}
