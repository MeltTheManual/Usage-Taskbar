using System.IO;

namespace Usage.App;

internal static class AppLog
{
    private static readonly string PathName =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Usage.log");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(
                PathName,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Never let logging take the chip down.
        }
    }
}
