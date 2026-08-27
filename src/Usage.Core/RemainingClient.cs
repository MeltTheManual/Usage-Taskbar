using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Usage.Core;

/// <summary>
/// Reads weekly and session remaining for Claude and Codex from the logins the official CLIs already keep
/// on this PC.
///
/// This type is strictly read-only with respect to those login files, and that is a deliberate safety rule
/// rather than an accident. Usage used to refresh expiring tokens and write them back. That code failed with
/// HTTP 400 on every single attempt it ever made, roughly thirty times across ninety minutes on 2026-08-16,
/// and the readings stayed correct throughout because the CLIs refresh their own tokens perfectly well. The
/// failure was in fact the only thing keeping it safe: Anthropic rotates refresh tokens as single use, so a
/// successful refresh here would have invalidated the copy Claude Code was holding and could have signed the
/// user out of the tool they were working in. An observer does not get to touch another program's credentials.
/// </summary>
public sealed class RemainingClient : IDisposable
{
    /// <summary>
    /// Under this, everything that can warn does: the chip turns red and the card turns amber. Deliberately
    /// one number rather than two that happen to agree. The chip and the card briefly carried separate
    /// thresholds, which left a thin sliver where a red taskbar sat beside a calm card. It was 20 before
    /// 2026-08-25.
    /// </summary>
    public const double LowRemainingThreshold = 25;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(3);

    internal const string ClaudeUsageUrl = "https://api.anthropic.com/api/oauth/usage";
    internal const string CodexUsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    private readonly HttpClient _http;
    private readonly string _claudePath;
    private readonly string _codexPath;
    private readonly Action<string>? _log;

    public RemainingClient(
        string? claudePath = null,
        string? codexPath = null,
        HttpClient? http = null,
        Action<string>? log = null)
    {
        _claudePath = claudePath ?? LoginPaths.ClaudeCredentials;
        _codexPath = codexPath ?? LoginPaths.CodexAuth;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _log = log;
    }

    public async Task<RemainingSnapshot> FetchAsync(
        bool includeClaude = true,
        bool includeCodex = true,
        CancellationToken cancellationToken = default)
    {
        var claude = includeClaude
            ? await FetchClaudeAsync(cancellationToken).ConfigureAwait(false)
            : ProviderReading.Hidden();
        var codex = includeCodex
            ? await FetchCodexAsync(cancellationToken).ConfigureAwait(false)
            : ProviderReading.Hidden();
        return new RemainingSnapshot(claude, codex);
    }

    public async Task<ProviderReading> FetchClaudeAsync(CancellationToken cancellationToken = default)
    {
        if (!TryReadClaude(out var accessToken, out var signIn, out var notInstalled))
        {
            if (notInstalled)
            {
                return ProviderReading.NotInstalled();
            }

            return signIn ? ProviderReading.SignIn() : ProviderReading.Unavailable();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ClaudeUsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        request.Headers.TryAddWithoutValidation("User-Agent", CliVersions.ClaudeUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Usually means the CLI has not refreshed its token yet. Claude Code fixes this on its own.
                _log?.Invoke($"claude usage rejected the token http={(int)response.StatusCode}");
                return ProviderReading.SignIn();
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"claude usage failed http={(int)response.StatusCode}");
                return ProviderReading.Unavailable();
            }

            using var doc = JsonDocument.Parse(body);
            var weekly = ReadClaudeWindow(doc.RootElement, "seven_day");
            var fiveHour = ReadClaudeWindow(doc.RootElement, "five_hour");
            if (weekly.Remaining is null && fiveHour.Remaining is null)
            {
                return ProviderReading.Unavailable();
            }

            return new ProviderReading(ReadingStatus.Ok, weekly.Remaining, weekly.ResetsAt, fiveHour.Remaining, fiveHour.ResetsAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke("claude usage " + ex.GetType().Name);
            return ProviderReading.Unavailable();
        }
    }

    public async Task<ProviderReading> FetchCodexAsync(CancellationToken cancellationToken = default)
    {
        if (!TryReadCodex(out var accessToken, out var accountId, out var signIn, out var notInstalled))
        {
            if (notInstalled)
            {
                return ProviderReading.NotInstalled();
            }

            return signIn ? ProviderReading.SignIn() : ProviderReading.Unavailable();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CodexUsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        request.Headers.TryAddWithoutValidation("User-Agent", CliVersions.CodexUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _log?.Invoke($"codex usage rejected the token http={(int)response.StatusCode}");
                return ProviderReading.SignIn();
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"codex usage failed http={(int)response.StatusCode}");
                return ProviderReading.Unavailable();
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("rate_limit", out var rateLimit) || rateLimit.ValueKind != JsonValueKind.Object)
            {
                return ProviderReading.Unavailable();
            }

            var weekly = ReadCodexWindow(rateLimit, weekly: true);
            var fiveHour = ReadCodexWindow(rateLimit, weekly: false);
            if (weekly.Remaining is null)
            {
                return ProviderReading.Unavailable();
            }

            return new ProviderReading(ReadingStatus.Ok, weekly.Remaining, weekly.ResetsAt, fiveHour.Remaining, fiveHour.ResetsAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke("codex usage " + ex.GetType().Name);
            return ProviderReading.Unavailable();
        }
    }

    public static double RemainingFromUsed(double usedPercent) => Math.Max(0, 100 - usedPercent);

    public static bool IsWeeklyWindow(int limitWindowSeconds) => limitWindowSeconds >= 6 * 24 * 60 * 60;

    public void Dispose() => _http.Dispose();

    private bool TryReadClaude(out string accessToken, out bool signIn, out bool notInstalled)
    {
        accessToken = "";
        signIn = false;
        notInstalled = false;

        // No login file means Claude Code is not on this machine, which is not a problem to report. It is only
        // a problem when the file exists and cannot be used.
        if (!File.Exists(_claudePath))
        {
            notInstalled = true;
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_claudePath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                signIn = true;
                return false;
            }

            if (!TryString(oauth, "accessToken", out accessToken))
            {
                signIn = true;
                return false;
            }

            // expiresAt is deliberately not consulted. It was observed to sit in the past for ninety minutes
            // while the endpoint kept answering normally, so the server's opinion is the only one that counts.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool TryReadCodex(out string accessToken, out string accountId, out bool signIn, out bool notInstalled)
    {
        accessToken = "";
        accountId = "";
        signIn = false;
        notInstalled = false;

        // Same rule as Claude: absent means not installed, present but unusable means sign in.
        if (!File.Exists(_codexPath))
        {
            notInstalled = true;
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_codexPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens))
            {
                signIn = true;
                return false;
            }

            if (!TryString(tokens, "access_token", out accessToken) || !TryString(tokens, "account_id", out accountId))
            {
                signIn = true;
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (double? Remaining, DateTimeOffset? ResetsAt) ReadClaudeWindow(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var window) || window.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (null, null);
        }

        if (!window.TryGetProperty("utilization", out var used) || used.ValueKind is JsonValueKind.Null)
        {
            return (null, null);
        }

        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resets_at", out var resetEl) && resetEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(resetEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            reset = parsed;
        }

        return (RemainingFromUsed(used.GetDouble()), reset);
    }

    private static (double? Remaining, DateTimeOffset? ResetsAt) ReadCodexWindow(JsonElement rateLimit, bool weekly)
    {
        foreach (var name in new[] { "primary_window", "secondary_window" })
        {
            if (!rateLimit.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!window.TryGetProperty("limit_window_seconds", out var secondsEl) || secondsEl.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            // Mapped by how long the window is, never by the name. The names have been observed to swap.
            if (IsWeeklyWindow(secondsEl.GetInt32()) != weekly)
            {
                continue;
            }

            if (!window.TryGetProperty("used_percent", out var usedEl))
            {
                return (null, null);
            }

            DateTimeOffset? reset = null;
            if (window.TryGetProperty("reset_at", out var resetAt) && resetAt.ValueKind == JsonValueKind.Number)
            {
                reset = DateTimeOffset.FromUnixTimeSeconds(resetAt.GetInt64());
            }
            else if (window.TryGetProperty("reset_after_seconds", out var after) && after.ValueKind == JsonValueKind.Number)
            {
                reset = DateTimeOffset.UtcNow.AddSeconds(after.GetInt32());
            }

            return (RemainingFromUsed(usedEl.GetDouble()), reset);
        }

        return (null, null);
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }
}
