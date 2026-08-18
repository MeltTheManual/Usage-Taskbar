namespace Usage.Core;

/// <summary>
/// Where Usage is installed, and where it is allowed to write. Those are deliberately two different places.
///
/// The installer lets people choose the install folder, and they may well choose Program Files, which a normal
/// user cannot write to. So nothing created at runtime may sit beside the exe. The install folder is simply
/// wherever the running exe happens to be, and anything the app writes goes to a per-user data folder instead.
///
/// This replaced a hardcoded <c>%LOCALAPPDATA%\Usage</c> path from the days when the exe copied itself into
/// place. The installer owns placement now, so the app must never assume it knows where it lives.
/// </summary>
public static class InstallLocation
{
    /// <summary>The running executable's full path.</summary>
    public static string Exe => Environment.ProcessPath ?? "";

    /// <summary>The folder the running executable sits in, whatever the installer chose.</summary>
    public static string Folder
    {
        get
        {
            var exe = Exe;
            return string.IsNullOrWhiteSpace(exe) ? "" : Path.GetDirectoryName(exe) ?? "";
        }
    }

    /// <summary>
    /// Per-user, always writable, and independent of the install folder. Holds the startup marker. Removed by
    /// the uninstaller, so a reinstall behaves like a first install.
    /// </summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Usage");
}
