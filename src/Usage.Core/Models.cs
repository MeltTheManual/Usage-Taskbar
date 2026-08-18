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
    NotInstalled
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

    /// <summary>True when this provider should not appear anywhere in the UI at all.</summary>
    public bool IsHidden => Status == ReadingStatus.NotInstalled;

    public bool IsLow => Status == ReadingStatus.Ok
        && WeeklyRemaining is { } remaining
        && remaining < RemainingClient.LowRemainingThreshold;
}

public sealed record RemainingSnapshot(ProviderReading Claude, ProviderReading Codex);

/// <summary>
/// One limit window ready to be drawn: what to call it, how much is left, and when it resets.
/// Only windows the provider actually reported ever become one of these, so the card can draw
/// whatever it is given without having to know that Codex reports no session window.
/// </summary>
public sealed record RemainingWindow(string Label, double Remaining, DateTimeOffset? ResetsAt)
{
    public bool IsLow => Remaining < RemainingClient.LowRemainingThreshold;
}
