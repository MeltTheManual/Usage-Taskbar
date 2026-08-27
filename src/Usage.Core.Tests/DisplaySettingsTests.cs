using Usage.Core;
using Xunit;

namespace Usage.Core.Tests;

public class DisplaySettingsTests : IDisposable
{
    private readonly string _folder;
    private readonly string _path;

    public DisplaySettingsTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "usage-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp folder is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Missing_file_shows_both_providers()
    {
        var settings = DisplaySettings.Load(_path);

        Assert.True(settings.ShowClaude);
        Assert.True(settings.ShowCodex);
    }

    [Fact]
    public void Round_trip_keeps_a_turned_off_provider_off()
    {
        var saved = new DisplaySettings { ShowClaude = false, ShowCodex = true };
        saved.Save(_path);

        var loaded = DisplaySettings.Load(_path);

        Assert.False(loaded.ShowClaude);
        Assert.True(loaded.ShowCodex);
    }

    [Fact]
    public void Partial_file_keeps_the_other_provider_on()
    {
        File.WriteAllText(_path, """{ "showClaude": false }""");

        var loaded = DisplaySettings.Load(_path);

        Assert.False(loaded.ShowClaude);
        Assert.True(loaded.ShowCodex);
    }

    [Fact]
    public void Broken_file_falls_back_to_showing_both()
    {
        File.WriteAllText(_path, "this is not json");

        var loaded = DisplaySettings.Load(_path);

        Assert.True(loaded.ShowClaude);
        Assert.True(loaded.ShowCodex);
    }

    [Fact]
    public void Apply_hides_a_turned_off_provider_and_leaves_the_other_alone()
    {
        var claude = new ProviderReading(ReadingStatus.Ok, 45, null, 88, null);
        var codex = new ProviderReading(ReadingStatus.Ok, 65, null, null, null);
        var snapshot = new RemainingSnapshot(claude, codex);
        var settings = new DisplaySettings { ShowClaude = false, ShowCodex = true };

        var visible = settings.Apply(snapshot);

        Assert.Equal(ReadingStatus.Hidden, visible.Claude.Status);
        Assert.True(visible.Claude.IsHidden);
        Assert.Equal("", ChipText.FormatProvider("Claude", visible.Claude));
        Assert.Empty(ChipText.Windows(visible.Claude));

        Assert.Equal(ReadingStatus.Ok, visible.Codex.Status);
        Assert.Equal(65, visible.Codex.WeeklyRemaining);
        Assert.False(visible.Codex.IsHidden);
    }

    [Fact]
    public void Apply_can_hide_both_without_calling_them_not_installed()
    {
        var snapshot = new RemainingSnapshot(
            new ProviderReading(ReadingStatus.SignIn, null, null, null, null),
            new ProviderReading(ReadingStatus.Ok, 65, null, null, null));
        var settings = new DisplaySettings { ShowClaude = false, ShowCodex = false };

        var visible = settings.Apply(snapshot);

        Assert.True(visible.Claude.IsHidden);
        Assert.True(visible.Codex.IsHidden);
        Assert.Equal(ReadingStatus.Hidden, visible.Claude.Status);
        Assert.Equal(ReadingStatus.Hidden, visible.Codex.Status);
        Assert.NotEqual(ReadingStatus.NotInstalled, visible.Claude.Status);
    }
}
