using Usage.Core;
using Xunit;

namespace Usage.Core.Tests;

public class CliVersionsTests : IDisposable
{
    private readonly string _folder;

    public CliVersionsTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "usage-ver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
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
    public void Reads_the_version_out_of_a_package_file()
    {
        var path = Write("""{ "name": "@anthropic-ai/claude-code", "version": "2.1.232" }""");
        Assert.Equal("2.1.232", CliVersions.ReadPackageVersion(path));
    }

    [Fact]
    public void Returns_nothing_when_the_package_file_is_missing_or_broken()
    {
        Assert.Null(CliVersions.ReadPackageVersion(Path.Combine(_folder, "nope.json")));
        Assert.Null(CliVersions.ReadPackageVersion(Write("not json at all")));
        Assert.Null(CliVersions.ReadPackageVersion(Write("""{ "name": "no version here" }""")));
    }

    [Fact]
    public void Rejects_a_version_that_could_poison_the_user_agent_header()
    {
        Assert.False(CliVersions.LooksLikeVersion("1.0\r\nX-Evil: yes"));
        Assert.False(CliVersions.LooksLikeVersion("2.1.232 (patched)"));
        Assert.False(CliVersions.LooksLikeVersion(""));
        Assert.False(CliVersions.LooksLikeVersion(null));
        Assert.False(CliVersions.LooksLikeVersion("no-digits-here"));

        Assert.True(CliVersions.LooksLikeVersion("2.1.232"));
        Assert.True(CliVersions.LooksLikeVersion("0.146.0"));
        Assert.True(CliVersions.LooksLikeVersion("1.0.0-beta.2"));
    }

    [Fact]
    public void Falls_back_to_the_pinned_version_rather_than_sending_nothing()
    {
        // Whatever this machine has installed, the User-Agent must always be a real CLI string.
        Assert.StartsWith("claude-code/", CliVersions.ClaudeUserAgent);
        Assert.StartsWith("codex_cli_rs/", CliVersions.CodexUserAgent);
        Assert.True(CliVersions.LooksLikeVersion(CliVersions.ClaudeCode));
        Assert.True(CliVersions.LooksLikeVersion(CliVersions.Codex));
    }

    private string Write(string contents)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, contents);
        return path;
    }
}
