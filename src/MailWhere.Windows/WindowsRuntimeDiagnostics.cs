using System.IO;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using MailWhere.Core.Capabilities;

namespace MailWhere.Windows;

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
        const string valueName = "MailWhereProbe";

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

        return Path.Combine(root, "MailWhere");
    }

    public static string GetFollowUpDatabasePath() =>
        Path.Combine(GetAppDataDirectory(), "followups.sqlite");

    public static void RecordUiEvent(string code, IReadOnlyDictionary<string, string>? details = null)
    {
        try
        {
            var directory = GetAppDataDirectory();
            Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(new
            {
                atUtc = DateTimeOffset.UtcNow,
                code,
                details = details ?? new Dictionary<string, string>()
            });
            File.AppendAllText(Path.Combine(directory, "ui-events.jsonl"), line + Environment.NewLine);
        }
        catch
        {
            // UI observability must never block the tray assistant.
        }
    }

    public static IReadOnlyList<string> GetFollowUpDatabaseResetPaths()
    {
        var databasePath = GetFollowUpDatabasePath();
        return new[]
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm"
        };
    }

    public static int DeleteFollowUpDatabaseFiles()
    {
        var deleted = 0;
        foreach (var path in GetFollowUpDatabaseResetPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            FileSystem.DeleteFile(path);
            deleted++;
        }

        return deleted;
    }
}
