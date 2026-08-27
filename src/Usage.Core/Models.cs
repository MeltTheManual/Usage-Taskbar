namespace Usage.Core;

public enum ReadingStatus
{
    Ok,
    SignIn,
    Unavailable,
    Stale,

    /// <summary>
    /// The tool is not on this machine at all, so there is no login file to read. Different from
    /// <see cref="SignIn"/>, which means the file is there but unusable and the user can do something about it.
    /// A provider nobody uses is hidden rather than nagged about, so someone who only runs Claude Code never
    /// sees a permanent "Codex sign in" telling them to install something they did not ask for.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// The user turned this provider off on the right-click menu. Different from <see cref="NotInstalled"/>:
    /// the tool may well be here, they just do not want it on the chip. Re-enable from the same menu.
    /// </summary>
    Hidden
}

/// <summary>
/// How full a provider still is, in the three steps the taskbar chip paints itself by.
///
/// The card does not paint itself in these three colours, but it does read the bottom band: its amber warning
/// starts at exactly the same moment the chip turns red, off exactly the same rounded number.
/// </summary>
public enum ChipLevel
{
    /// <summary>Under 25% left. Red, and meant to be read as a warning.</summary>
    Low,

    /// <summary>25% up to 75% left. Yellow.</summary>
    Fair,

    /// <summary>75% or more left. A soft light green that is not trying to catch the eye.</summary>
    Plenty
}

public sealed record ProviderReading(
    ReadingStatus Status,
    double? WeeklyRemaining,
    DateTimeOffset? WeeklyResetsAt,
    double? FiveHourRemaining,
    DateTimeOffset? FiveHourResetsAt)
{
    public static ProviderReading SignIn() =>
        new(ReadingStatus.SignIn, null, null, null, null);

    public static ProviderReading Unavailable() =>
        new(ReadingStatus.Unavailable, null, null, null, null);

    public static ProviderReading Stale() =>
        new(ReadingStatus.Stale, null, null, null, null);

    public static ProviderReading NotInstalled() =>
        new(ReadingStatus.NotInstalled, null, null, null, null);

    public static ProviderReading Hidden() =>
        new(ReadingStatus.Hidden, null, null, null, null);

    /// <summary>
    /// True when this provider should not appear on the chip, the hover card, or the menu number line.
    /// Covers a tool that is not installed, and a tool the user turned off.
    /// </summary>
    public bool IsHidden => Status is ReadingStatus.NotInstalled or ReadingStatus.Hidden;

    /// <summary>
    /// True once this provider is low enough to warn about. It defers to the chip's own band function, so the
    /// card can never warn at a different moment, or off a different number, than the taskbar text does.
    /// </summary>
    public bool IsLow => Status == ReadingStatus.Ok
        && WeeklyRemaining is { } remaining
        && ChipText.LevelFor(remaining) == ChipLevel.Low;
}

public sealed record RemainingSnapshot(ProviderReading Claude, ProviderReading Codex);

/// <summary>
/// One limit window ready to be drawn: what to call it, how much is left, and when it resets.
/// Only windows the provider actually reported ever become one of these, so the card can draw
/// whatever it is given without having to know that Codex reports no session window.
/// </summary>
public sealed record RemainingWindow(string Label, double Remaining, DateTimeOffset? ResetsAt)
{
    public bool IsLow => ChipText.LevelFor(Remaining) == ChipLevel.Low;
}
