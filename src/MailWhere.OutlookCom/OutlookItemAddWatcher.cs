using System.Runtime.InteropServices;
using MailWhere.Core.Domain;

namespace MailWhere.OutlookCom;

public sealed record OutlookItemAddedEvent(MailSourceFolder SourceFolder, string? SourceId);

public sealed class OutlookItemAddWatcher : IDisposable
{
    private static readonly Guid ItemsEventsInterfaceId = new("00063077-0000-0000-C000-000000000046");
    private const int ItemAddDispatchId = 61441;

    private readonly List<FolderSubscription> _subscriptions = new();
    private object? _outlook;
    private object? _session;
    private bool _disposed;

    public event EventHandler<OutlookItemAddedEvent>? ItemAdded;

    public static OutlookItemAddWatcher Start()
    {
        var watcher = new OutlookItemAddWatcher();
        try
        {
            watcher.Initialize();
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false)
                          ?? throw new InvalidOperationException("OutlookProgIdUnavailable");
        _outlook = Activator.CreateInstance(outlookType)
                   ?? throw new InvalidOperationException("OutlookCreateInstanceReturnedNull");
        dynamic outlookDynamic = _outlook;
        _session = outlookDynamic.Session;

        SubscribeDefaultFolder(6, MailSourceFolder.Inbox);
        SubscribeDefaultFolder(5, MailSourceFolder.Sent);
    }

    private void SubscribeDefaultFolder(int folderId, MailSourceFolder folder)
    {
        if (_session is null)
        {
            return;
        }

        object? folderObject = null;
        object? items = null;
        try
        {
            folderObject = _session.GetType().InvokeMember(
                "GetDefaultFolder",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                _session,
                new object[] { folderId });
            if (folderObject is null)
            {
                return;
            }

            items = folderObject.GetType().InvokeMember(
                "Items",
                System.Reflection.BindingFlags.GetProperty,
                null,
                folderObject,
                null);
            if (items is null)
            {
                return;
            }

            Action<object> handler = item => OnItemAdded(folder, item);
            ComEventsHelper.Combine(items, ItemsEventsInterfaceId, ItemAddDispatchId, handler);
            _subscriptions.Add(new FolderSubscription(folderObject, items, handler));
            folderObject = null;
            items = null;
        }
        finally
        {
            ComRelease.FinalRelease(items);
            ComRelease.FinalRelease(folderObject);
        }
    }

    private void OnItemAdded(MailSourceFolder folder, object item)
    {
        if (_disposed)
        {
            return;
        }

        string? sourceId = null;
        try
        {
            sourceId = Convert.ToString(item.GetType().InvokeMember(
                "EntryID",
                System.Reflection.BindingFlags.GetProperty,
                null,
                item,
                null));
        }
        catch
        {
            // The fallback scan can still catch the item even if EntryID is not readable at event time.
        }

        ItemAdded?.Invoke(this, new OutlookItemAddedEvent(folder, sourceId));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            try
            {
                ComEventsHelper.Remove(subscription.Items, ItemsEventsInterfaceId, ItemAddDispatchId, subscription.Handler);
            }
            catch
            {
                // Best-effort COM event cleanup.
            }

            ComRelease.FinalRelease(subscription.Items);
            ComRelease.FinalRelease(subscription.Folder);
        }

        _subscriptions.Clear();
        ComRelease.FinalRelease(_session);
        ComRelease.FinalRelease(_outlook);
        _session = null;
        _outlook = null;
    }

    private sealed record FolderSubscription(object Folder, object Items, Action<object> Handler);
}
