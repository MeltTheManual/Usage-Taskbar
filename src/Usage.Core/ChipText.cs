using System.Globalization;

namespace Usage.Core;

public static class ChipText
{
    /// <summary>The short segment shown on the taskbar chip, and the line the probe prints.</summary>
    public static string FormatProvider(string name, ProviderReading reading)
    {
        return reading.Status switch
        {
            ReadingStatus.Ok when reading.WeeklyRemaining is { } remaining =>
                $"{name} {Whole(remaining)}%",
            // The tool is not on this machine, so there is nothing honest to say about it. Callers drop it.
            ReadingStatus.NotInstalled => "",
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
    /// </summary>
    public static IReadOnlyList<RemainingWindow> Windows(ProviderReading reading)
    {
        if (reading.Status != ReadingStatus.Ok)
        {
            return [];
        }

        var windows = new List<RemainingWindow>();
        if (reading.WeeklyRemaining is { } weekly)
        {
            windows.Add(new RemainingWindow("This week", weekly, reading.WeeklyResetsAt));
        }

        if (reading.FiveHourRemaining is { } session)
        {
            windows.Add(new RemainingWindow("This 5 hours", session, reading.FiveHourResetsAt));
        }

        return windows;
    }

    /// <summary>What the card says instead of meters when there is nothing honest to draw.</summary>
    public static string StatusLine(ProviderReading reading) => reading.Status switch
    {
        ReadingStatus.NotInstalled => "Not installed on this PC",
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

    private static string Whole(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
