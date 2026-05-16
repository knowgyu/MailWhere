using Microsoft.Win32;
using MailWhere.Core.Capabilities;

namespace MailWhere.Windows;

internal enum StartupRegistrationResultKind
{
    Applied,
    SkippedNonWindows,
    Failed
}

internal sealed record StartupRegistrationResult(
    StartupRegistrationResultKind Kind,
    string? FailureCode = null)
{
    public bool Succeeded =>
        Kind is StartupRegistrationResultKind.Applied or StartupRegistrationResultKind.SkippedNonWindows;
}

internal static class WindowsStartupRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MailWhere";

    public static StartupRegistrationResult ApplyRequestedState(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new StartupRegistrationResult(StartupRegistrationResultKind.SkippedNonWindows);
        }

        try
        {
            SetStartupRegistration(enabled);
            return new StartupRegistrationResult(StartupRegistrationResultKind.Applied);
        }
        catch (Exception ex)
        {
            return new StartupRegistrationResult(StartupRegistrationResultKind.Failed, ex.GetType().Name);
        }
    }

    public static string? BuildStartupCommand(string? exePath)
        => StartupLaunchModeResolver.BuildTrayStartupCommand(exePath);

    private static void SetStartupRegistration(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("RegistryRunKeyUnavailable");
        }

        if (enabled)
        {
            var command = BuildStartupCommand(Environment.ProcessPath);
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException("ProcessPathUnavailable");
            }

            key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
