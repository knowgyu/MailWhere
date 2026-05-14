namespace OutlookAiSecretary.Core.Notifications;

public sealed class NotificationThrottle
{
    private readonly TimeSpan _minimumInterval;
    private readonly Dictionary<string, DateTimeOffset> _lastNotified = new(StringComparer.Ordinal);

    public NotificationThrottle(TimeSpan? minimumInterval = null)
    {
        _minimumInterval = minimumInterval ?? TimeSpan.FromHours(4);
    }

    public bool ShouldNotify(string sourceIdHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sourceIdHash))
        {
            return false;
        }

        if (!_lastNotified.TryGetValue(sourceIdHash, out var last))
        {
            _lastNotified[sourceIdHash] = now;
            return true;
        }

        if (now - last < _minimumInterval)
        {
            return false;
        }

        _lastNotified[sourceIdHash] = now;
        return true;
    }
}
