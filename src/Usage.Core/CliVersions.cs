using System.Text.Json;

namespace Usage.Core;

/// <summary>
/// Reads the installed Claude Code and Codex versions so the User-Agent we send matches the real CLI.
/// A stale User-Agent can get the usage endpoints to rate-limit us, so a pinned string is only the fallback.
/// </summary>
public static class CliVersions
{
    public const string ClaudeFallback = "2.1.232";
    public const string CodexFallback = "0.146.0";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    private static readonly object Gate = new();

    private static string? _claude;
    private static string? _codex;
    private static DateTimeOffset _readAt = DateTimeOffset.MinValue;

    public static string ClaudeCode => Current().Claude;

    public static string Codex => Current().Codex;

    public static string ClaudeUserAgent => "claude-code/" + ClaudeCode;

    public static string CodexUserAgent => "codex_cli_rs/" + Codex;

    /// <summary>Forgets the cached versions. Used by tests.</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _readAt = DateTimeOffset.MinValue;
        }
    }

    internal static bool LooksLikeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            return false;
        }

        var digits = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
                continue;
            }

            if (c is not ('.' or '-' or '+') && !char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return digits > 0;
    }

    internal static string? ReadPackageVersion(string packageJsonPath)
    {
        try
        {
            if (!File.Exists(packageJsonPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (!doc.RootElement.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = version.GetString();
            return LooksLikeVersion(text) ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (string Claude, string Codex) Current()
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - _readAt < CacheFor && _claude is not null && _codex is not null)
            {
                return (_claude, _codex);
            }

            var npm = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "node_modules");

            _claude = ReadPackageVersion(Path.Combine(npm, "@anthropic-ai", "claude-code", "package.json"))
                ?? ClaudeFallback;
            _codex = ReadPackageVersion(Path.Combine(npm, "@openai", "codex", "package.json"))
                ?? CodexFallback;
            _readAt = DateTimeOffset.UtcNow;
            return (_claude, _codex);
        }
    }
}
