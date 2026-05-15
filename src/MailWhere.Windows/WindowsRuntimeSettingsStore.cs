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
}
