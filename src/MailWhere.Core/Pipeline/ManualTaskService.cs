using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Pipeline;

public sealed class ManualTaskService
{
    private readonly IFollowUpStore _store;
    private readonly TimeProvider _timeProvider;

    public ManualTaskService(IFollowUpStore store, TimeProvider? timeProvider = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LocalTaskItem> CreateAsync(string title, DateTimeOffset? dueAt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Manual task title is required.", nameof(title));
        }

        var now = _timeProvider.GetUtcNow();
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            title.Trim(),
            dueAt,
            null,
            null,
            1.0,
            "Manual task",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: "직접 추가",
            SourceReceivedAt: now,
            Kind: FollowUpKind.ActionRequested);

        await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return task;
    }
}
