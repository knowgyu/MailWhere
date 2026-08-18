namespace MailWhere.Core.Capabilities;

public enum StartupLaunchMode
{
    ShowMainWindow,
    TrayOnly,
    OpenSourceToken
}

public static class StartupLaunchModeResolver
{
    public static StartupLaunchMode FromArgs(IEnumerable<string> args)
    {
        var values = args.ToArray();
        if (TryGetOpenSourceToken(values, out _))
        {
            return StartupLaunchMode.OpenSourceToken;
        }

        return values.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase))
            ? StartupLaunchMode.TrayOnly
            : StartupLaunchMode.ShowMainWindow;
    }

    public static bool TryGetOpenSourceToken(IEnumerable<string> args, out string token)
    {
        var values = args.ToArray();
        for (var i = 0; i < values.Length - 1; i++)
        {
            if (string.Equals(values[i], "--open-source-token", StringComparison.OrdinalIgnoreCase))
            {
                token = values[i + 1];
                return true;
            }
        }

        token = string.Empty;
        return false;
    }

    public static string? BuildTrayStartupCommand(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        return $"\"{exePath.Trim().Trim('"')}\" --tray";
    }

    public static bool MatchesExecutable(string? configuredCommand, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(configuredCommand) || string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var normalizedConfigured = configuredCommand.Trim();
        var normalizedPath = exePath.Trim().Trim('"');
        if (normalizedConfigured.StartsWith('"'))
        {
            var closingQuote = normalizedConfigured.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return string.Equals(
                    normalizedConfigured[1..closingQuote],
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        var firstToken = normalizedConfigured.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(firstToken?.Trim('"'), normalizedPath, StringComparison.OrdinalIgnoreCase);
    }
}
