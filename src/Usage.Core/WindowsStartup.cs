using Microsoft.Win32;

namespace Usage.Core;

public static class WindowsStartup
{
    public const string RunValueName = "Usage";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Written once the first time Usage runs. Its presence is what stops the watcher from silently
    /// putting the Run key back after the user deliberately turns "Start with Windows" off.
    /// </summary>
    private static string MarkerPath => Path.Combine(InstallLocation.DataFolder, ".startup-configured");

    public static void Register(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Usage executable was not found, so Windows startup was not registered.");
        }

        var quoted = $"\"{Path.GetFullPath(executablePath)}\" --watch";
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("Could not open the current-user Run key.");
        key.SetValue(RunValueName, quoted);
        WriteMarker();
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        WriteMarker();
    }

    public static bool IsRegistered() => !string.IsNullOrWhiteSpace(GetRegisteredCommand());

    public static string? GetRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) as string;
    }

    /// <summary>
    /// Turns startup on for a brand new install only. Once the user has made a choice either way the marker
    /// exists and this does nothing, so an unchecked "Start with Windows" actually stays unchecked.
    /// </summary>
    public static void EnsureFirstRunRegistration(string executablePath)
    {
        try
        {
            if (File.Exists(MarkerPath))
            {
                return;
            }

            Register(executablePath);
        }
        catch (Exception)
        {
            // Startup registration is a convenience, never a reason to stop the app from running.
        }
    }

    private static void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(InstallLocation.DataFolder);
            File.WriteAllText(MarkerPath, "Usage has asked about Windows startup once. Delete to be asked again.");
        }
        catch (Exception)
        {
            // A missing marker only means the first-run default may reapply. Not worth failing over.
        }
    }
}
