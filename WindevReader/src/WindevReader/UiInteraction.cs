namespace WindevReader;

/// <summary>Observed serialized state codes; combinations and other codes are not inferred.</summary>
public enum WindevControlState : uint
{
    Active = 0,
    Inactive = 1,
    GrayedOut = 4
}

/// <summary>Initial settings and their positions in the decoded property buffer.</summary>
public sealed record WindevUiInteraction(
    int VisibilityOffset,
    uint RawVisibility,
    int? StateOffset,
    uint? RawStateCode,
    int? TabAccessOffset,
    uint? RawTabAccess)
{
    public bool? IsVisible => DecodeBoolean(RawVisibility);

    public WindevControlState? State => RawStateCode switch
    {
        0 => WindevControlState.Active,
        1 => WindevControlState.Inactive,
        4 => WindevControlState.GrayedOut,
        _ => null
    };

    public bool? IsTabStop => DecodeBoolean(RawTabAccess);

    private static bool? DecodeBoolean(uint? value) => value switch
    {
        0 => false,
        1 => true,
        _ => null
    };
}
