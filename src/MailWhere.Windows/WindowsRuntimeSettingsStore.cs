using System.IO;
using MailWhere.Core.Capabilities;

namespace MailWhere.Windows;

internal static class WindowsRuntimeSettingsStore
{
    public static string SettingsPath => Path.Combine(WindowsRuntimeDiagnostics.GetAppDataDirectory(), "runtime-settings.json");

    public static RuntimeSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var seeded = LoadSeedDefaults();
                if (seeded is not null)
                {
                    Save(seeded);
                    return seeded;
                }

                return RuntimeSettings.ManagedSafeDefault;
            }

            var json = File.ReadAllText(SettingsPath);
            return RuntimeSettingsSerializer.ParseOrDefault(json);
        }
        catch
        {
            return RuntimeSettings.ManagedSafeDefault;
        }
    }

    public static void Save(RuntimeSettings settings)
    {
        Directory.CreateDirectory(WindowsRuntimeDiagnostics.GetAppDataDirectory());
        File.WriteAllText(SettingsPath, RuntimeSettingsSerializer.Serialize(settings));
    }

    private static RuntimeSettings? LoadSeedDefaults()
    {
        foreach (var path in SeedDefaultPaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = File.ReadAllText(path);
                return RuntimeSettingsSerializer.ParseOrDefault(json);
            }
            catch
            {
                // Ignore broken deployment seed files and continue with safe defaults.
            }
        }

        return null;
    }

    private static IEnumerable<string> SeedDefaultPaths()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "MailWhere.defaults.json");
        yield return Path.Combine(baseDirectory, "config", "MailWhere.defaults.json");
    }
}
