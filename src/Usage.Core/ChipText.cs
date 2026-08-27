using System.Globalization;

namespace Usage.Core;

public static class ChipText
{
    /// <summary>At or above this, the chip is light green.</summary>
    public const double PlentyThreshold = 75;

    // The bottom boundary is RemainingClient.LowRemainingThreshold rather than a second constant here, so the
    // chip and the card can never drift apart again.

    /// <summary>The short segment shown on the taskbar chip, and the line the probe prints.</summary>
    public static string FormatProvider(string name, ProviderReading reading)
    {
        return reading.Status switch
        {
            ReadingStatus.Ok when reading.WeeklyRemaining is { } remaining =>
                $"{name} {Whole(remaining)}%",
            // Not on this machine, or the user turned it off. Callers drop either one the same way.
            ReadingStatus.NotInstalled or ReadingStatus.Hidden => "",
            ReadingStatus.SignIn => $"{name} sign in",
            ReadingStatus.Stale => $"{name} stale",
            _ => $"{name} --"
        };
    }

    /// <summary>The descriptive line shown at the top of the right-click menu.</summary>
    public static string MenuText(string name, ProviderReading reading)
    {
        if (reading.Status != ReadingStatus.Ok || reading.WeeklyRemaining is not { } remaining)
        {
            return FormatProvider(name, reading);
        }

        var line = $"{name}  {Whole(remaining)}% this week";
        if (reading.FiveHourRemaining is { } session)
        {
            line += $"  ·  {Whole(session)}% this 5 hours";
        }

        return line;
    }

    /// <summary>
    /// The limit windows to draw on the hover card. A window the provider did not report never appears here
    /// at all, which is why Codex has one row and Claude has two. Empty when the reading itself failed.
    ///
    /// Shortest window first, and deliberately so: the five-hour limit is the one that will actually stop you
    /// today, so it belongs where the eye lands, and the weekly sits underneath it as the wider context.
    /// Codex reports no session window, so its single weekly row is unaffected.
    /// </summary>
    public static IReadOnlyList<RemainingWindow> Windows(ProviderReading reading)
    {
        if (reading.Status != ReadingStatus.Ok)
        {
            return [];
        }

        var windows = new List<RemainingWindow>();
        if (reading.FiveHourRemaining is { } session)
        {
            windows.Add(new RemainingWindow("This 5 hours", session, reading.FiveHourResetsAt));
        }

        if (reading.WeeklyRemaining is { } weekly)
        {
            windows.Add(new RemainingWindow("This week", weekly, reading.WeeklyResetsAt));
        }

        return windows;
    }

    /// <summary>What the card says instead of meters when there is nothing honest to draw.</summary>
    public static string StatusLine(ProviderReading reading) => reading.Status switch
    {
        ReadingStatus.NotInstalled => "Not installed on this PC",
        ReadingStatus.Hidden => "Hidden",
        ReadingStatus.SignIn => "Sign in needed",
        ReadingStatus.Stale => "Last check failed, so no number is shown",
        ReadingStatus.Ok => "No limits reported",
        _ => "Unavailable right now"
    };

    /// <summary>"resets Wed 19 Aug, 3:00 AM", or nothing at all when no reset time was reported.</summary>
    public static string ResetText(DateTimeOffset? resetsAt)
    {
        if (resetsAt is not { } reset)
        {
            return "";
        }

        return "resets " + reset.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string Percent(double value) => Whole(value) + "%";

    /// <summary>
    /// Which of the three colour bands the chip should paint a provider in.
    ///
    /// It bands on the rounded number rather than the raw one, so the colour can never contradict the digits
    /// printed right beside it. 74.6 prints as "75%", and a chip reading 75% in the sub-75 colour would look
    /// like a bug to anyone who noticed it.
    ///
    /// This is also what decides <see cref="ProviderReading.IsLow"/>, so the card warns at exactly the moment
    /// the chip turns red, on exactly the same rounded number.
    /// </summary>
    public static ChipLevel LevelFor(double remaining)
    {
        var shown = Rounded(remaining);

        if (shown >= PlentyThreshold)
        {
            return ChipLevel.Plenty;
        }

        return shown >= RemainingClient.LowRemainingThreshold ? ChipLevel.Fair : ChipLevel.Low;
    }

    private static string Whole(double value) =>
        Rounded(value).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The displayed number. Anything deciding a colour must use this, never the raw reading.</summary>
    internal static double Rounded(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
