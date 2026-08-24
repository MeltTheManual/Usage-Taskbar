using Usage.Core;
using Xunit;

namespace Usage.Core.Tests;

public class ChipTextTests
{
    [Fact]
    public void Formats_whole_remaining_percent()
    {
        var reading = new ProviderReading(ReadingStatus.Ok, 24.4, null, null, null);
        Assert.Equal("Codex 24%", ChipText.FormatProvider("Codex", reading));
    }

    [Fact]
    public void Formats_sign_in_without_a_number()
    {
        Assert.Equal("Claude sign in", ChipText.FormatProvider("Claude", ProviderReading.SignIn()));
        // A tool that is not on this PC produces no text at all, so callers can drop it without special cases.
        Assert.Equal("", ChipText.FormatProvider("Codex", ProviderReading.NotInstalled()));
        Assert.Equal("", ChipText.MenuText("Codex", ProviderReading.NotInstalled()));
    }

    [Fact]
    public void Marks_low_remaining()
    {
        var reading = new ProviderReading(ReadingStatus.Ok, 24, null, null, null);
        Assert.True(reading.IsLow);
        Assert.False(new ProviderReading(ReadingStatus.Ok, 25, null, null, null).IsLow);

        // The card warns off the same rounded number as the chip, so 24.5 prints "25%" and must not warn.
        Assert.False(new ProviderReading(ReadingStatus.Ok, 24.5, null, null, null).IsLow);
        Assert.True(new ProviderReading(ReadingStatus.Ok, 24.4, null, null, null).IsLow);
    }

    [Fact]
    public void Chip_colour_bands_split_at_seventy_five_and_twenty_five()
    {
        Assert.Equal(ChipLevel.Plenty, ChipText.LevelFor(100));
        Assert.Equal(ChipLevel.Plenty, ChipText.LevelFor(75));
        Assert.Equal(ChipLevel.Fair, ChipText.LevelFor(74));
        Assert.Equal(ChipLevel.Fair, ChipText.LevelFor(25));
        Assert.Equal(ChipLevel.Low, ChipText.LevelFor(24));
        Assert.Equal(ChipLevel.Low, ChipText.LevelFor(0));
    }

    [Fact]
    public void Chip_colour_follows_the_number_the_chip_actually_prints()
    {
        // 74.6 prints as "75%", so it has to be the 75-and-up colour or the chip contradicts itself.
        var reading = new ProviderReading(ReadingStatus.Ok, 74.6, null, null, null);
        Assert.Equal("Claude 75%", ChipText.FormatProvider("Claude", reading));
        Assert.Equal(ChipLevel.Plenty, ChipText.LevelFor(74.6));

        // Same rule at the bottom: 24.5 prints as "25%" and must not be the under-25 colour.
        Assert.Equal("Claude 25%", ChipText.FormatProvider("Claude", new ProviderReading(ReadingStatus.Ok, 24.5, null, null, null)));
        Assert.Equal(ChipLevel.Fair, ChipText.LevelFor(24.5));
        Assert.Equal(ChipLevel.Low, ChipText.LevelFor(24.4));
    }

    [Fact]
    public void Converts_used_percent_to_remaining()
    {
        Assert.Equal(24, RemainingClient.RemainingFromUsed(76));
        Assert.True(RemainingClient.IsWeeklyWindow(604800));
        Assert.False(RemainingClient.IsWeeklyWindow(18000));
    }

    [Fact]
    public void Hover_shows_a_meter_for_each_window_that_was_reported()
    {
        // Claude reports a five-hour window, so its card draws a session meter as well as a weekly one.
        var reading = new ProviderReading(
            ReadingStatus.Ok,
            45,
            DateTimeOffset.Parse("2026-08-18T22:00:00Z"),
            33,
            DateTimeOffset.Parse("2026-08-15T20:40:00Z"));

        var windows = ChipText.Windows(reading);

        // Shortest window first, because the five-hour limit is the one that stops you today.
        Assert.Equal(2, windows.Count);
        Assert.Equal("This 5 hours", windows[0].Label);
        Assert.Equal(33, windows[0].Remaining);
        Assert.Equal("This week", windows[1].Label);
        Assert.Equal(45, windows[1].Remaining);
    }

    [Fact]
    public void Hover_leaves_out_a_session_window_that_was_never_reported()
    {
        // Codex reported no five-hour window. A missing window must vanish, never appear as an empty meter.
        var reading = new ProviderReading(ReadingStatus.Ok, 14, DateTimeOffset.Parse("2026-08-20T10:49:56Z"), null, null);

        var windows = ChipText.Windows(reading);

        Assert.Single(windows);
        Assert.Equal("This week", windows[0].Label);
        Assert.DoesNotContain(windows, w => w.Label.Contains("5 hours", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_window_below_the_threshold_is_marked_low_so_its_meter_turns_amber()
    {
        Assert.True(new RemainingWindow("This week", 2, null).IsLow);
        Assert.True(new RemainingWindow("This 5 hours", 24, null).IsLow);
        Assert.False(new RemainingWindow("This week", 25, null).IsLow);
        Assert.False(new RemainingWindow("This 5 hours", 88, null).IsLow);
    }

    [Fact]
    public void Reset_text_is_omitted_when_the_source_did_not_give_one()
    {
        Assert.Equal("", ChipText.ResetText(null));
        Assert.StartsWith("resets ", ChipText.ResetText(DateTimeOffset.Parse("2026-08-18T22:00:00Z")));
    }

    [Fact]
    public void Hover_draws_no_meters_at_all_when_the_reading_failed()
    {
        foreach (var reading in new[] { ProviderReading.SignIn(), ProviderReading.Stale(), ProviderReading.Unavailable() })
        {
            Assert.Empty(ChipText.Windows(reading));
            Assert.NotEmpty(ChipText.StatusLine(reading));
        }

        Assert.Equal("Sign in needed", ChipText.StatusLine(ProviderReading.SignIn()));
    }

    [Fact]
    public void Percent_is_a_whole_number()
    {
        Assert.Equal("45%", ChipText.Percent(44.6));
        Assert.Equal("2%", ChipText.Percent(2));
        Assert.Equal("100%", ChipText.Percent(100));
    }

    [Fact]
    public void Menu_line_adds_the_session_window_only_when_it_was_reported()
    {
        var withSession = new ProviderReading(ReadingStatus.Ok, 49, null, 88, null);
        Assert.Contains("88% this 5 hours", ChipText.MenuText("Claude", withSession));

        var withoutSession = new ProviderReading(ReadingStatus.Ok, 17, null, null, null);
        Assert.DoesNotContain("5 hours", ChipText.MenuText("Codex", withoutSession));
    }
}
