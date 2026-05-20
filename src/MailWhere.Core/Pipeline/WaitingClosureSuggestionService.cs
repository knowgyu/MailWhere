using System.Text.RegularExpressions;
using MailWhere.Core.Analysis;
using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Pipeline;

public sealed class WaitingClosureSuggestionService
{
    private static readonly Regex Acknowledgement = new(
        "(확인했습니다|확인하였습니다|수령했습니다|수령하였습니다|감사합니다|고맙습니다|받았습니다|잘 받았습니다|확인 후 진행|thanks|thank you|received|got it|looks good)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IFollowUpStore _store;
    private readonly IWaitingClosureJudge _judge;
    private readonly TimeProvider _timeProvider;

    public WaitingClosureSuggestionService(IFollowUpStore store, IWaitingClosureJudge judge, TimeProvider? timeProvider = null)
    {
        _store = store;
        _judge = judge;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> CreateSuggestionsAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0)
        {
            return 0;
        }

        var waitingTasks = (await _store.ListOpenTasksAsync(cancellationToken).ConfigureAwait(false))
            .Where(task => task.Kind == FollowUpKind.WaitingForReply && !string.IsNullOrWhiteSpace(task.SourceConversationId))
            .ToArray();
        if (waitingTasks.Length == 0)
        {
            return 0;
        }

        var created = 0;
        foreach (var email in emails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var task in waitingTasks.Where(task => IsSameConversation(task, email)))
            {
                if (TryBuildTrigger(task, email) is not { } trigger)
                {
                    continue;
                }

                var judgment = await _judge.JudgeAsync(trigger, cancellationToken).ConfigureAwait(false);
                if (!judgment.ShouldSuggest)
                {
                    continue;
                }

                var suggestion = new WaitingClosureSuggestion(
                    Guid.NewGuid(),
                    task.Id,
                    FollowUpPresentation.ActionTitle(task.Title),
                    email.SourceHash,
                    trigger.Kind,
                    judgment.Source,
                    judgment.Confidence,
                    judgment.Reason,
                    email.ReceivedAt,
                    _timeProvider.GetUtcNow());
                if (await _store.SaveWaitingClosureSuggestionAsync(suggestion, cancellationToken).ConfigureAwait(false))
                {
                    created++;
                }
            }
        }

        return created;
    }

    private static bool IsSameConversation(LocalTaskItem task, EmailSnapshot email) =>
        !string.IsNullOrWhiteSpace(email.ConversationId)
        && string.Equals(task.SourceConversationId, email.ConversationId, StringComparison.Ordinal)
        && !string.Equals(task.SourceIdHash, email.SourceHash, StringComparison.Ordinal);

    private static WaitingClosureTrigger? TryBuildTrigger(LocalTaskItem task, EmailSnapshot email)
    {
        var senderKey = ReplyProgressMatcher.NormalizeParticipantKey(email.SenderDisplay);
        if (senderKey.Length == 0)
        {
            return null;
        }

        var ownerKey = ReplyProgressMatcher.NormalizeParticipantKey(email.MailboxOwnerDisplayName);
        var sentByOwner = ownerKey.Length > 0 && string.Equals(senderKey, ownerKey, StringComparison.Ordinal);
        if (sentByOwner && Acknowledgement.IsMatch($"{email.Subject}\n{email.Body}"))
        {
            return new WaitingClosureTrigger(task, email, WaitingClosureTriggerKind.UserAcknowledgement, 0.86, "사용자가 확인/감사 답장을 보낸 것으로 보입니다.");
        }

        var expectedKeys = task.SourceRecipientDisplayNames?
            .Select(ReplyProgressMatcher.NormalizeParticipantKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (expectedKeys?.Contains(senderKey) == true)
        {
            return new WaitingClosureTrigger(task, email, WaitingClosureTriggerKind.RecipientReply, 0.72, "요청한 상대의 회신이 감지되었습니다.");
        }

        return null;
    }
}
