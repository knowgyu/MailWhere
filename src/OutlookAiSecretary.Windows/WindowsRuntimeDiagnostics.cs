using System.IO;
using Microsoft.Win32;
using OutlookAiSecretary.Core.Capabilities;

namespace OutlookAiSecretary.Windows;

internal static class WindowsRuntimeDiagnostics
{
    public static CapabilityProbeResult ProbeStorageWritable()
    {
        try
        {
            var directory = GetAppDataDirectory();
            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(directory, ".write-probe.tmp");
            using (var stream = new FileStream(probePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes("ok");
                stream.Write(bytes, 0, bytes.Length);
            }

            return CapabilityProbeResult.Passed(
                "storage-writable",
                "StorageWritable",
                new Dictionary<string, string>
                {
                    ["feature"] = "local-storage",
                    ["statusCode"] = "writable"
                });
        }
        catch (Exception ex)
        {
            return CapabilityProbeResult.Failed(
                "storage-writable",
                "StorageWriteFailed",
                CapabilitySeverity.Blocked,
                new Dictionary<string, string>
                {
                    ["feature"] = "local-storage",
                    ["statusCode"] = "not-writable",
                    ["errorClass"] = ex.GetType().Name
                });
        }
    }

    public static CapabilityProbeResult ProbeStartupToggleWritable()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "OutlookAiSecretaryProbe";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (key is null)
            {
                return CapabilityProbeResult.Failed(
                    "startup-toggle",
                    "StartupRegistryUnavailable",
                    CapabilitySeverity.Degraded,
                    new Dictionary<string, string>
                    {
                        ["feature"] = "startup-toggle",
                        ["statusCode"] = "registry-unavailable"
                    });
            }

            key.SetValue(valueName, "\"probe\"");
            key.DeleteValue(valueName, throwOnMissingValue: false);

            return CapabilityProbeResult.Passed(
                "startup-toggle",
                "StartupRegistryWritable",
                new Dictionary<string, string>
                {
                    ["feature"] = "startup-toggle",
                    ["statusCode"] = "writable"
                });
        }
        catch (Exception ex)
        {
            return CapabilityProbeResult.Failed(
                "startup-toggle",
                "StartupRegistryWriteFailed",
                CapabilitySeverity.Degraded,
                new Dictionary<string, string>
                {
                    ["feature"] = "startup-toggle",
                    ["statusCode"] = "not-writable",
                    ["errorClass"] = ex.GetType().Name
                });
        }
    }

    public static CapabilityProbeResult ProbeRuntimeSettings(RuntimeSettings settings) =>
        CapabilityProbeResult.Passed(
            "runtime-settings",
            "RuntimeSettingsLoaded",
            new Dictionary<string, string>
            {
                ["feature"] = "runtime-settings",
                ["mode"] = settings.AutomaticWatcherRequested ? "watcher-requested" : "manual"
            });

    public static string GetAppDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "OutlookAiSecretary");
    }
}
