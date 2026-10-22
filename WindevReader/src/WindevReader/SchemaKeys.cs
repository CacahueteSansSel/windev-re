namespace WindevReader;

/// <summary>Key kinds verified in the supplied WDD 28 profile and corroborated in version 31.</summary>
public enum WindevKeyKind : ushort
{
    None = 0,
    Duplicates = 2,
    Primary = 4
}

/// <summary>An ordered component resolved through the table's zero-based field directory.</summary>
public sealed record WindevKeyComponent(
    ushort FieldOrdinal,
    uint FieldId,
    string FieldName,
    byte RawOptions,
    int Offset);

/// <summary>All offsets refer to the decoded WDD schema, including its four-byte revision prefix.</summary>
public sealed record WindevFieldKey(
    int Offset,
    ushort RawKind,
    int? ComponentsOffset,
    bool ComponentsDecoded,
    IReadOnlyList<WindevKeyComponent> Components)
{
    public WindevKeyKind? Kind => RawKind switch
    {
        0 => WindevKeyKind.None,
        2 => WindevKeyKind.Duplicates,
        4 => WindevKeyKind.Primary,
        _ => null
    };

    public bool? IsPrimaryKey => Kind is WindevKeyKind kind ? kind == WindevKeyKind.Primary : null;

    public bool? AllowsDuplicates => Kind switch
    {
        WindevKeyKind.Duplicates => true,
        WindevKeyKind.Primary => false,
        _ => null
    };
}
