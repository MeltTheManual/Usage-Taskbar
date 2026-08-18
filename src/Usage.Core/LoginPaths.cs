namespace Usage.Core;

public static class LoginPaths
{
    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ClaudeCredentials =>
        Path.Combine(UserProfile, ".claude", ".credentials.json");

    public static string CodexAuth =>
        Path.Combine(UserProfile, ".codex", "auth.json");
}
