using System.IO;
using System.Windows;
using MailWhere.Core.Search;
using MailWhere.OutlookCom;
using MailWhere.Storage;
using Microsoft.Data.Sqlite;

namespace MailWhere.Windows;

internal static class OpenSourceTokenLaunchHandler
{
    public static async Task<int> OpenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!MailMirrorOpenSourceToken.IsValid(token))
        {
            return Report(false, "invalid-open-source-token");
        }

        var databasePath = Path.Combine(WindowsRuntimeDiagnostics.GetAppDataDirectory(), "followups.sqlite");
        if (!File.Exists(databasePath))
        {
            return Report(false, "database-not-found");
        }

        await using var store = new SqliteMailMirrorStore(databasePath);
        try
        {
            await store.InitializeAsync(cancellationToken);
        }
        catch (SqliteException)
        {
            return Report(false, "open-source-token-migration-failed");
        }
        catch (IOException)
        {
            return Report(false, "open-source-token-database-unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            return Report(false, "open-source-token-database-unavailable");
        }

        var locator = await store.ResolveOpenSourceTokenAsync(token, cancellationToken);
        if (locator is null || !locator.IsValid)
        {
            return Report(false, "open-source-token-not-found");
        }

        var result = await new OutlookComMailOpener().OpenAsync(locator.StoreId, locator.EntryId, cancellationToken);
        return Report(result.Success, result.StatusCode);
    }

    private static int Report(bool success, string statusCode)
    {
        Environment.ExitCode = success ? 0 : 2;
        System.Windows.MessageBox.Show(
            success ? "MailWhere opened the original mail." : $"MailWhere could not open the original mail: {statusCode}",
            "MailWhere open source token",
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        return Environment.ExitCode;
    }
}
