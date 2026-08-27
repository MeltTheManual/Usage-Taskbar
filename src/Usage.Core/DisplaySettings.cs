using System.Text.Json;

namespace Usage.Core;

/// <summary>
/// Which providers appear on the chip and the hover card. Both on by default, matching the original app.
/// Turning one off hides it everywhere a reading would show, and skips fetching it, so a provider you do not
/// want to see is not polled either.
///
/// Lives in the per-user data folder, not beside the exe, because Program Files is not writable.
/// </summary>
public sealed class DisplaySettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public bool ShowClaude { get; set; } = true;
    public bool ShowCodex { get; set; } = true;

    public static string DefaultPath => Path.Combine(InstallLocation.DataFolder, "settings.json");

    public static DisplaySettings Load() => Load(DefaultPath);

    public static DisplaySettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new DisplaySettings();
            }

            var loaded = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(path), JsonOptions);
            return loaded ?? new DisplaySettings();
        }
        catch (Exception)
        {
            // A broken settings file must not blank the chip. Fall back to showing both, the original behaviour.
            return new DisplaySettings();
        }
    }

    public void Save() => Save(DefaultPath);

    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>
    /// Replaces a turned-off provider with <see cref="ReadingStatus.Hidden"/> so the chip, the card and the
    /// menu headers all drop it through the existing IsHidden path.
    /// </summary>
    public RemainingSnapshot Apply(RemainingSnapshot snapshot) =>
        new(
            ShowClaude ? snapshot.Claude : ProviderReading.Hidden(),
            ShowCodex ? snapshot.Codex : ProviderReading.Hidden());
}
