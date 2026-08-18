using System.Net;
using System.Text.Json;
using Usage.Core;
using Xunit;

namespace Usage.Core.Tests;

/// <summary>
/// Covers the JSON parsing, which is the part that actually breaks when Anthropic or OpenAI rename a field.
/// </summary>
public class RemainingClientTests : IDisposable
{
    private readonly string _folder;
    private readonly string _claudePath;
    private readonly string _codexPath;

    public RemainingClientTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "usage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _claudePath = Path.Combine(_folder, ".credentials.json");
        _codexPath = Path.Combine(_folder, "auth.json");
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
    public async Task Claude_turns_utilization_into_remaining()
    {
        WriteClaudeLogin();
        var body = """
        {
          "seven_day": { "utilization": 51, "resets_at": "2026-08-18T22:00:00Z" },
          "five_hour": { "utilization": 12, "resets_at": "2026-08-15T20:40:00Z" }
        }
        """;

        var reading = await NewClient(FakeHandler.Ok(body)).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(49, reading.WeeklyRemaining);
        Assert.Equal(88, reading.FiveHourRemaining);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-18T22:00:00Z"),
            reading.WeeklyResetsAt!.Value);
    }

    [Fact]
    public async Task Claude_hides_a_five_hour_window_that_was_not_reported()
    {
        WriteClaudeLogin();
        var reading = await NewClient(FakeHandler.Ok("""{ "seven_day": { "utilization": 30 } }"""))
            .FetchClaudeAsync();

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(70, reading.WeeklyRemaining);
        Assert.Null(reading.FiveHourRemaining);
        Assert.Null(reading.FiveHourResetsAt);
    }

    [Fact]
    public async Task Claude_says_sign_in_when_the_token_is_rejected()
    {
        WriteClaudeLogin();
        var reading = await NewClient(FakeHandler.Status(HttpStatusCode.Unauthorized)).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.SignIn, reading.Status);
        Assert.Null(reading.WeeklyRemaining);
    }

    [Fact]
    public async Task Claude_says_unavailable_when_the_server_errors()
    {
        WriteClaudeLogin();
        var reading = await NewClient(FakeHandler.Status(HttpStatusCode.InternalServerError)).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.Unavailable, reading.Status);
        Assert.Null(reading.WeeklyRemaining);
    }

    [Fact]
    public async Task Claude_is_treated_as_not_installed_when_there_is_no_login_file()
    {
        // No file at all means the tool is not on this PC. That must not read as "sign in", because the user
        // is not being asked to fix anything. Someone who only runs Codex should never see a Claude row.
        var reading = await NewClient(FakeHandler.Ok("{}")).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.NotInstalled, reading.Status);
        Assert.True(reading.IsHidden);
    }

    [Fact]
    public async Task Codex_is_treated_as_not_installed_when_there_is_no_login_file()
    {
        var reading = await NewClient(FakeHandler.Ok("{}")).FetchCodexAsync();

        Assert.Equal(ReadingStatus.NotInstalled, reading.Status);
        Assert.True(reading.IsHidden);
    }

    [Fact]
    public async Task A_login_file_that_exists_but_cannot_be_used_still_says_sign_in()
    {
        // The difference that matters: the file is here, so something is wrong and the user can act on it.
        File.WriteAllText(_claudePath, "{\"somethingElse\":true}");
        var reading = await NewClient(FakeHandler.Ok("{}")).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.SignIn, reading.Status);
        Assert.False(reading.IsHidden);
    }

    [Fact]
    public async Task Each_provider_is_judged_only_by_its_own_login_file()
    {
        // Having one tool installed must never make the other one appear. This is what keeps a machine with
        // only Claude Code from showing a Codex row it can say nothing honest about.
        WriteClaudeLogin();
        var snapshot = await NewClient(FakeHandler.Ok("""{ "seven_day": { "utilization": 30 } }"""))
            .FetchAsync();

        Assert.Equal(ReadingStatus.Ok, snapshot.Claude.Status);
        Assert.False(snapshot.Claude.IsHidden);
        Assert.True(snapshot.Codex.IsHidden);
    }

    [Fact]
    public async Task Codex_maps_windows_by_length_not_by_name()
    {
        // The weekly window is deliberately in "secondary_window" here. Trusting the names would fail this test.
        WriteCodexLogin();
        var body = """
        {
          "rate_limit": {
            "primary_window": { "limit_window_seconds": 18000, "used_percent": 40 },
            "secondary_window": { "limit_window_seconds": 604800, "used_percent": 83 }
          }
        }
        """;

        var reading = await NewClient(FakeHandler.Ok(body)).FetchCodexAsync();

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(17, reading.WeeklyRemaining);
        Assert.Equal(60, reading.FiveHourRemaining);
    }

    [Fact]
    public async Task Codex_hides_a_missing_five_hour_window()
    {
        WriteCodexLogin();
        var body = """
        {
          "rate_limit": {
            "primary_window": { "limit_window_seconds": 604800, "used_percent": 83, "reset_at": 1787000000 }
          }
        }
        """;

        var reading = await NewClient(FakeHandler.Ok(body)).FetchCodexAsync();

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(17, reading.WeeklyRemaining);
        Assert.Null(reading.FiveHourRemaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787000000), reading.WeeklyResetsAt!.Value);
    }

    [Fact]
    public async Task Codex_refuses_to_guess_when_the_weekly_window_is_absent()
    {
        WriteCodexLogin();
        var body = """
        {
          "rate_limit": {
            "primary_window": { "limit_window_seconds": 18000, "used_percent": 40 }
          }
        }
        """;

        var reading = await NewClient(FakeHandler.Ok(body)).FetchCodexAsync();

        Assert.Equal(ReadingStatus.Unavailable, reading.Status);
        Assert.Null(reading.WeeklyRemaining);
    }

    [Fact]
    public async Task A_broken_response_never_produces_a_number()
    {
        WriteClaudeLogin();
        var reading = await NewClient(FakeHandler.Ok("this is not json")).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.Unavailable, reading.Status);
        Assert.Null(reading.WeeklyRemaining);
    }

    [Fact]
    public async Task Never_writes_to_the_login_files()
    {
        // The safety rule of the whole app. Usage observes another program's credentials and must never
        // touch them. It used to refresh and write them back, which risked signing the user out of the CLI it
        // was borrowing from. Expired tokens here, because that is exactly when the old code wrote.
        WriteClaudeLogin(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        WriteCodexLogin();

        var claudeBefore = File.ReadAllBytes(_claudePath);
        var codexBefore = File.ReadAllBytes(_codexPath);
        var claudeStamp = File.GetLastWriteTimeUtc(_claudePath);
        var codexStamp = File.GetLastWriteTimeUtc(_codexPath);

        // Any POST at all would mean a refresh is being attempted, so fail loudly if one shows up.
        var handler = new FakeHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return FakeHandler.Json(request.RequestUri!.Host.Contains("anthropic")
                ? """{ "seven_day": { "utilization": 10 } }"""
                : """{ "rate_limit": { "primary_window": { "limit_window_seconds": 604800, "used_percent": 10 } } }""");
        });

        var snapshot = await NewClient(handler).FetchAsync();

        Assert.Equal(ReadingStatus.Ok, snapshot.Claude.Status);
        Assert.Equal(ReadingStatus.Ok, snapshot.Codex.Status);

        Assert.Equal(claudeBefore, File.ReadAllBytes(_claudePath));
        Assert.Equal(codexBefore, File.ReadAllBytes(_codexPath));
        Assert.Equal(claudeStamp, File.GetLastWriteTimeUtc(_claudePath));
        Assert.Equal(codexStamp, File.GetLastWriteTimeUtc(_codexPath));

        // No stray temp file either. One would briefly hold real token material.
        Assert.Empty(Directory.GetFiles(_folder, "*.usage-tmp"));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public async Task An_expired_looking_token_is_still_offered_to_the_server()
    {
        // The server's opinion is the only one that counts. On 2026-08-16 the stored expiresAt sat in the
        // past for ninety minutes while the endpoint kept answering normally, so Usage must not pre-judge.
        WriteClaudeLogin(expiresAt: DateTimeOffset.UtcNow.AddHours(-3));

        var reading = await NewClient(FakeHandler.Ok("""{ "seven_day": { "utilization": 55 } }""")).FetchClaudeAsync();

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(45, reading.WeeklyRemaining);
    }

    private RemainingClient NewClient(FakeHandler handler) =>
        new(_claudePath, _codexPath, new HttpClient(handler));

    private void WriteClaudeLogin(DateTimeOffset? expiresAt = null)
    {
        var expiry = (expiresAt ?? DateTimeOffset.UtcNow.AddHours(4)).ToUnixTimeMilliseconds();
        File.WriteAllText(_claudePath, $$"""
        {
          "claudeAiOauth": {
            "accessToken": "original-access",
            "refreshToken": "original-refresh",
            "expiresAt": {{expiry}},
            "subscriptionType": "keep-me"
          },
          "somethingElse": "other-tool-value"
        }
        """);
    }

    private void WriteCodexLogin()
    {
        File.WriteAllText(_codexPath, """
        {
          "tokens": {
            "access_token": "original-access",
            "refresh_token": "original-refresh",
            "account_id": "acct_123"
          }
        }
        """);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public static FakeHandler Ok(string body) => new(_ => Json(body));

        public static FakeHandler Status(HttpStatusCode code) => new(_ => new HttpResponseMessage(code)
        {
            Content = new StringContent("")
        });

        public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_respond(request));
    }
}
