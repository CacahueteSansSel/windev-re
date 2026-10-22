namespace WindevReader;

/// <summary>Control families verified in serialized WDW records, not input subtypes or all WLanguage constants.</summary>
public enum WindevControlType : uint
{
    Input = 2,
    Label = 3,
    Button = 4,
    CheckBox = 5,
    RadioButton = 6,
    Image = 8,
    Table = 9,
    ComboBox = 14,
    Tab = 16
}

/// <summary>A bounded record in the decoded property buffer. Specific content is currently decoded for version 28.</summary>
public sealed record WindevUiControlRecord(
    int Offset,
    int ByteLength,
    uint RawTypeCode,
    uint RawDescriptor,
    int CommonPropertiesOffset,
    int CommonPropertiesByteLength,
    int SpecificPropertiesOffset)
{
    public WindevControlType? Type => RawTypeCode switch
    {
        2 => WindevControlType.Input,
        3 => WindevControlType.Label,
        4 => WindevControlType.Button,
        5 => WindevControlType.CheckBox,
        6 => WindevControlType.RadioButton,
        8 => WindevControlType.Image,
        9 => WindevControlType.Table,
        14 => WindevControlType.ComboBox,
        16 => WindevControlType.Tab,
        _ => null
    };

    public IReadOnlyList<WindevUiItem> Items { get; init; } = Array.Empty<WindevUiItem>();

    public bool ItemsDecoded { get; init; }

    public WindevString? RawItemsText { get; init; }

    public WindevString? ImageSource { get; init; }

    public WindevUiTableBinding? TableBinding { get; init; }

    public IReadOnlyList<WindevUiTabPage> TabPages { get; init; } = Array.Empty<WindevUiTabPage>();

    public bool TabPagesDecoded { get; init; }
}

/// <summary>Text positions exclude framing, the terminating NUL and line separators. Metadata remains uninterpreted.</summary>
public sealed record WindevUiItem(string Text, int TextOffset, int TextByteLength, int? RecordOffset, int? MetadataOffset);

public sealed record WindevUiTableBinding(
    WindevString TableName,
    WindevString BrowseKey,
    uint DeclaredColumnCount,
    IReadOnlyList<WindevUiTableColumn> Columns)
{
    public bool ColumnsDecoded => Columns.Count == DeclaredColumnCount;
}

/// <summary>Column width and editor codes are distinct from ordinary control rectangles and HFSQL field types.</summary>
public sealed record WindevUiTableColumn(
    WindevString Name,
    int DescriptorOffset,
    ulong RawIdentifier,
    uint Width,
    WindevString Caption,
    WindevString DataBinding,
    uint RawEditorTypeCode);

/// <summary>Child offsets identify bounded control records; the page identifier and remaining flags are not interpreted.</summary>
public sealed record WindevUiTabPage(
    int Offset,
    WindevString Caption,
    ulong RawIdentifier,
    IReadOnlyList<int> ChildRecordOffsets);
