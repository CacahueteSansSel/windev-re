namespace WindevReader.Internal;

internal static class SchemaKeyReader
{
    private const int maximumComponentCount = 10_000;

    internal static IReadOnlyList<WindevDataField> ResolveComponents(
        ReadOnlySpan<byte> data,
        IReadOnlyList<WindevDataField> fields,
        WindevOpenOptions options,
        ref int totalComponents)
    {
        List<WindevDataField> result = new List<WindevDataField>();

        foreach (WindevDataField field in fields)
        {
            if (!field.IsCompositeKey || field.Key is not WindevFieldKey key)
            {
                result.Add(field);

                continue;
            }

            int suffixOffset = key.Offset - 27;

            Binary.Require(data, suffixOffset, 54);

            int directoryOffset = Binary.Offset(Binary.U64(data, suffixOffset + 46), data.Length);
            int count = Binary.Count(Binary.U16(data, directoryOffset), maximumComponentCount, directoryOffset);

            if (count == 0 || count > options.MaxIndexEntries - totalComponents)
            {
                throw new WindevFormatException("Invalid or over-budget composite key component count", directoryOffset);
            }

            Binary.Require(data, directoryOffset + 2, count * 3);

            List<WindevKeyComponent> components = new List<WindevKeyComponent>();

            for (int componentIndex = 0; componentIndex < count; componentIndex++)
            {
                int entryOffset = directoryOffset + 2 + componentIndex * 3;
                ushort fieldOrdinal = Binary.U16(data, entryOffset);

                if (fieldOrdinal >= fields.Count || fields[fieldOrdinal].Id == field.Id)
                {
                    throw new WindevFormatException("Composite key component does not reference another field of this table", entryOffset);
                }

                WindevDataField componentField = fields[fieldOrdinal];
                int componentSuffixOffset = componentField.Key!.Offset - 27;

                if (Binary.U16(data, componentSuffixOffset + 23) != fieldOrdinal ||
                    Binary.U16(data, componentSuffixOffset + 25) != fieldOrdinal)
                {
                    throw new UnsupportedWindevFormatException("Composite ordinal mapping is unvalidated for reordered field metadata", entryOffset);
                }

                components.Add(new WindevKeyComponent(
                    fieldOrdinal,
                    componentField.Id,
                    componentField.Name,
                    data[entryOffset + 2],
                    entryOffset));
            }

            totalComponents += count;

            WindevFieldKey resolvedKey = key with
            {
                ComponentsOffset = directoryOffset,
                ComponentsDecoded = true,
                Components = components.AsReadOnly()
            };

            result.Add(field with { Key = resolvedKey });
        }

        return result.AsReadOnly();
    }
}
