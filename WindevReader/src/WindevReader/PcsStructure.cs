using WindevReader.Internal;

namespace WindevReader;

public sealed record WindevPcsSectionRange(int Index, byte Tag, long Offset, uint StoredByteLength, long End, bool InBounds);
public sealed record WindevPcsBlockLocation(int Offset, int StoredByteLength, uint DecodedByteLength, int? SectionIndex, bool AfterDeclaredSections);
public sealed record WindevPcsContainerLocation(int Offset, ushort WindevVersion, uint DocumentType, uint SectionCount, long PayloadOffset, long DeclaredEnd);
public sealed record WindevPcsWindowDirectoryCandidate(int DirectoryOffset, long InferredPayloadOffset,
    long DeclaredEnd, IReadOnlyList<WindevPcsSectionRange> Sections);
public sealed record WindevPcsStructureReport(
    int FileByteLength,
    ushort WindevVersion,
    uint DocumentType,
    uint ContainerRevision,
    uint SectionCount,
    long PayloadOffset,
    long DeclaredEnd,
    long TrailingByteLength,
    IReadOnlyList<WindevPcsSectionRange> Sections,
    IReadOnlyList<WindevPcsBlockLocation> ValidatedLzwBlocks,
    IReadOnlyList<WindevPcsContainerLocation> EmbeddedContainers,
    int RejectedLzwCandidates,
    bool LzwScanLimited)
{
    public long ValidationDecodedByteBudgetUsed { get; init; }
    public int ValidationAttempts { get; init; }
    public IReadOnlyList<WindevPcsWindowDirectoryCandidate> WindowDirectoryCandidates { get; init; } =
        Array.Empty<WindevPcsWindowDirectoryCandidate>();
}

/// <summary>Numeric structural diagnostics only. No source, strings, identifiers or raw byte segments are returned.</summary>
public static class WindevPcsStructure
{
    public static WindevPcsStructureReport Read(string filename, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();
        options.Validate();
        using FileStream input = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (input.Length > options.MaxFileBytes)
        {
            throw new WindevFormatException("File exceeds reader limit");
        }

        byte[] data = new byte[(int)input.Length];
        input.ReadExactly(data);

        return Inspect(data, options);
    }

    public static WindevPcsStructureReport Inspect(ReadOnlySpan<byte> data, WindevOpenOptions? options = null)
    {
        options ??= new WindevOpenOptions();
        options.Validate();

        if (data.Length > options.MaxFileBytes)
        {
            throw new WindevFormatException("File exceeds reader limit");
        }

        Binary.Require(data, 0, 85);

        if (!data[..4].SequenceEqual("PCS\0"u8) || Binary.U32(data, 4) != 20)
        {
            throw new UnsupportedWindevFormatException("Structural inspection requires PCS revision 20");
        }

        int count = Binary.Count(Binary.U32(data, 81), 256, 81);
        Binary.Require(data, 85, count * 5);
        long payload = 32L + Binary.U32(data, 32);
        long cursor = payload;
        List<WindevPcsSectionRange> sections = new List<WindevPcsSectionRange>();

        for (int index = 0; index < count; index++)
        {
            uint length = Binary.U32(data, 86 + index * 5);
            long end = cursor + length;
            sections.Add(new WindevPcsSectionRange(index, data[85 + index * 5], cursor, length, end,
                cursor >= 85 + count * 5 && end <= data.Length));
            cursor = end;
        }

        List<WindevPcsBlockLocation> blocks = new List<WindevPcsBlockLocation>();
        List<WindevPcsContainerLocation> embedded = new List<WindevPcsContainerLocation>();
        int attempts = 0;
        int rejected = 0;
        long decodedBudget = 0;
        bool limited = false;

        for (int offset = 0; offset <= data.Length - 11; offset++)
        {
            if (offset > 0 && offset <= data.Length - 85 && data.Slice(offset, 4).SequenceEqual("PCS\0"u8) &&
                Binary.U32(data, offset + 4) == 20)
            {
                uint nestedCount = Binary.U32(data, offset + 81);
                long nestedPayload = offset + 32L + Binary.U32(data, offset + 32);

                if (nestedCount > 0 && nestedCount <= 256 && offset + 85L + nestedCount * 5 <= data.Length &&
                    nestedPayload >= offset + 85L + nestedCount * 5 && nestedPayload <= data.Length)
                {
                    long end = nestedPayload;

                    for (int index = 0; index < nestedCount; index++)
                    {
                        end += Binary.U32(data, offset + 86 + index * 5);
                    }

                    if (end <= data.Length && embedded.Count < 256)
                    {
                        embedded.Add(new WindevPcsContainerLocation(offset, Binary.U16(data, offset + 14),
                            Binary.U32(data, offset + 8), nestedCount, nestedPayload, end));
                    }
                }
            }

            if (data[offset] != 4 || data[offset + 9] != 0 || data[offset + 10] != 0)
            {
                continue;
            }

            uint stored = Binary.U32(data, offset + 1);
            uint decoded = Binary.U32(data, offset + 5);

            if (stored < 2 || stored > data.Length - offset - 11 || decoded > options.MaxDecodedBlockBytes)
            {
                continue;
            }

            if (limited || attempts >= Math.Min(256, options.MaxDecodedBlocks) ||
                decoded > options.MaxTotalDecodedBytes - decodedBudget)
            {
                limited = true;
                continue;
            }

            attempts++;
            decodedBudget += decoded;

            try
            {
                PcsContainer.DecodeLzwWrapper(data, offset, options, out int storedLength);
                int? sectionIndex = null;

                foreach (WindevPcsSectionRange section in sections)
                {
                    if (offset >= section.Offset && offset + (long)storedLength <= section.End)
                    {
                        sectionIndex = section.Index;
                        break;
                    }
                }

                blocks.Add(new WindevPcsBlockLocation(offset, storedLength, decoded, sectionIndex, offset >= cursor));
            }
            catch (WindevFormatException)
            {
                rejected++;
            }
        }

        return new WindevPcsStructureReport(data.Length, Binary.U16(data, 14), Binary.U32(data, 8), Binary.U32(data, 4),
            (uint)count, payload, cursor, Math.Max(0, data.Length - cursor), sections.AsReadOnly(), blocks.AsReadOnly(),
            embedded.AsReadOnly(), rejected, limited)
        {
            WindowDirectoryCandidates = FindWindowDirectories(data, cursor, blocks),
            ValidationDecodedByteBudgetUsed = decodedBudget,
            ValidationAttempts = attempts
        };
    }

    private static IReadOnlyList<WindevPcsWindowDirectoryCandidate> FindWindowDirectories(
        ReadOnlySpan<byte> data, long declaredEnd, IReadOnlyList<WindevPcsBlockLocation> blocks)
    {
        List<WindevPcsWindowDirectoryCandidate> candidates = new List<WindevPcsWindowDirectoryCandidate>();

        if (declaredEnd < 0 || declaredEnd > data.Length - 60)
        {
            return candidates.AsReadOnly();
        }

        byte[] expectedTags = new byte[] { 0, 17, 2, 0, 8, 19, 0, 5, 25, 10, 0, 6 };
        Dictionary<int, WindevPcsBlockLocation> validated = blocks.ToDictionary(block => block.Offset);
        int limit = (int)Math.Min(data.Length - 60L, declaredEnd + 65536);

        for (int offset = (int)declaredEnd; offset <= limit && candidates.Count < 8; offset++)
        {
            bool tagsMatch = true;
            long total = 0;

            for (int index = 0; index < expectedTags.Length; index++)
            {
                if (data[offset + index * 5] != expectedTags[index])
                {
                    tagsMatch = false;
                    break;
                }

                total += Binary.U32(data, offset + index * 5 + 1);
            }

            long payload = data.Length - total;

            if (!tagsMatch || payload < offset + 60 || payload > offset + 60L + 1024)
            {
                continue;
            }

            long cursor = payload;
            List<WindevPcsSectionRange> sections = new List<WindevPcsSectionRange>();

            for (int index = 0; index < expectedTags.Length; index++)
            {
                uint length = Binary.U32(data, offset + index * 5 + 1);
                sections.Add(new WindevPcsSectionRange(index, expectedTags[index], cursor, length, cursor + length, true));
                cursor += length;
            }

            // Infer a candidate only when property, source and preview wrappers independently agree.
            bool anchorsMatch = true;

            foreach ((int index, int prefix) in new (int, int)[] { (1, 28), (2, 4), (4, 0) })
            {
                WindevPcsSectionRange section = sections[index];
                long blockOffset = section.Offset + prefix;

                if (blockOffset > int.MaxValue || !validated.TryGetValue((int)blockOffset, out WindevPcsBlockLocation? block) ||
                    blockOffset + block.StoredByteLength != section.End)
                {
                    anchorsMatch = false;
                    break;
                }
            }

            if (anchorsMatch)
            {
                candidates.Add(new WindevPcsWindowDirectoryCandidate(offset, payload, cursor, sections.AsReadOnly()));
            }
        }

        return candidates.AsReadOnly();
    }
}
