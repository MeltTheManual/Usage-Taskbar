using Usage.Core;

var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var claudePath = GetArg(args, "--claude") ?? Path.Combine(userProfile, ".claude", ".credentials.json");
var codexPath = GetArg(args, "--codex") ?? Path.Combine(userProfile, ".codex", "auth.json");

using var client = new RemainingClient(claudePath, codexPath);

Console.WriteLine("Usage remaining probe");
Console.WriteLine("Prints remaining only. Never prints tokens.");
Console.WriteLine();

var snapshot = await client.FetchAsync();
WriteProvider("CLAUDE", snapshot.Claude);
Console.WriteLine();
WriteProvider("CODEX", snapshot.Codex);
return 0;

static void WriteProvider(string name, ProviderReading reading)
{
    Console.WriteLine(name);
    Console.WriteLine($"status     {reading.Status}");
    Console.WriteLine($"chip       {ChipText.FormatProvider(name == "CLAUDE" ? "Claude" : "Codex", reading)}");
    WriteWindow("weekly", reading.WeeklyRemaining, reading.WeeklyResetsAt);
    WriteWindow("five_hour", reading.FiveHourRemaining, reading.FiveHourResetsAt);
}

static void WriteWindow(string label, double? remaining, DateTimeOffset? reset)
{
    if (remaining is null)
    {
        Console.WriteLine($"{label,-10} not_reported");
        return;
    }

    var resetText = reset?.ToString("yyyy-MM-ddTHH:mm:ssK") ?? "not_reported";
    Console.WriteLine($"{label,-10} {remaining.Value:0.###}% remaining  reset {resetText}");
}

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}
