using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;
using MailWhere.Core.Pipeline;

namespace MailWhere.Windows;

public partial class ReviewCandidatesWindow : Window
{
    private IReadOnlyList<ReviewCandidate> _candidates;
    private IReadOnlyList<WaitingClosureSuggestion> _closureSuggestions;
    private readonly Func<ReviewCandidate, Task> _approveAsync;
    private readonly Func<ReviewCandidate, Task> _openMailAsync;
    private readonly Func<ReviewCandidate, Task> _snoozeAsync;
    private readonly Func<ReviewCandidate, Task> _ignoreAsync;
    private readonly Func<WaitingClosureSuggestion, bool, Task> _resolveClosureAsync;
    private readonly Func<Task<ReviewCandidateRetrySummary>> _retryLlmFailuresAsync;
    private readonly HashSet<Guid> _busyRowIds = new();
    private bool _canRetryLlmFailures;

    public ReviewCandidatesWindow(
        IReadOnlyList<ReviewCandidate> candidates,
        IReadOnlyList<WaitingClosureSuggestion> closureSuggestions,
        Func<ReviewCandidate, Task> approveAsync,
        Func<ReviewCandidate, Task> openMailAsync,
        Func<ReviewCandidate, Task> snoozeAsync,
        Func<ReviewCandidate, Task> ignoreAsync,
        Func<WaitingClosureSuggestion, bool, Task> resolveClosureAsync,
        Func<Task<ReviewCandidateRetrySummary>> retryLlmFailuresAsync,
        bool canRetryLlmFailures)
    {
        InitializeComponent();
        _candidates = candidates;
        _closureSuggestions = closureSuggestions;
        _approveAsync = approveAsync;
        _openMailAsync = openMailAsync;
        _snoozeAsync = snoozeAsync;
        _ignoreAsync = ignoreAsync;
        _resolveClosureAsync = resolveClosureAsync;
        _retryLlmFailuresAsync = retryLlmFailuresAsync;
        _canRetryLlmFailures = canRetryLlmFailures;
        Render();
    }

    public void Refresh(
        IReadOnlyList<ReviewCandidate> candidates,
        IReadOnlyList<WaitingClosureSuggestion>? closureSuggestions = null,
        bool? canRetryLlmFailures = null)
    {
        _candidates = candidates;
        if (closureSuggestions is not null)
        {
            _closureSuggestions = closureSuggestions;
        }

        if (canRetryLlmFailures is not null)
        {
            _canRetryLlmFailures = canRetryLlmFailures.Value;
        }

        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var rows = _closureSuggestions
            .Select(suggestion => ReviewWorkRow.FromClosureSuggestion(suggestion, _busyRowIds.Contains(suggestion.Id)))
            .Concat(_candidates.Select(candidate => ReviewWorkRow.FromCandidate(candidate, now, _busyRowIds.Contains(candidate.Id))))
            .ToArray();
        CandidatesList.ItemsSource = rows;
        CandidatesList.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length > 0 && CandidatesList.SelectedIndex < 0)
        {
            CandidatesList.SelectedIndex = 0;
        }

        var hasRetryableFailure = _candidates.Any(candidate => candidate.Analysis.IsTransientLlmFailureReview);
        RetryLlmFailuresButton.IsEnabled = hasRetryableFailure && _canRetryLlmFailures;
        StatusText.Text = BuildStatus(rows.Length, _candidates.Count, _closureSuggestions.Count, hasRetryableFailure);
    }

    private string BuildStatus(int totalRows, int candidateCount, int closureCount, bool hasRetryableFailure)
    {
        if (totalRows == 0)
        {
            return "표시할 확인 필요 항목이 없습니다.";
        }

        var parts = new List<string> { $"확인 필요 {totalRows}개" };
        if (closureCount > 0)
        {
            parts.Add($"보관 제안 {closureCount}개");
        }

        if (candidateCount > 0)
        {
            parts.Add($"검토 후보 {candidateCount}개");
        }

        if (hasRetryableFailure && _canRetryLlmFailures)
        {
            parts.Add("실패한 AI 분석 다시 시도 가능");
        }
        else if (hasRetryableFailure)
        {
            parts.Add("AI 설정이 꺼져 다시 시도 비활성화");
        }

        return string.Join(" · ", parts);
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await RunPrimaryAsync(sender);
    private async void OpenMail_Click(object sender, RoutedEventArgs e) => await RunOpenAsync(sender);
    private async void Snooze_Click(object sender, RoutedEventArgs e) => await RunCandidateAsync(sender, _snoozeAsync, "내일까지 다시 표시하지 않습니다.");
    private async void Ignore_Click(object sender, RoutedEventArgs e) => await RunSecondaryAsync(sender);

    private async void RetryLlmFailures_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RetryLlmFailuresButton.IsEnabled = false;
            StatusText.Text = "AI 분석을 다시 시도하는 중입니다…";
            var summary = await _retryLlmFailuresAsync();
            StatusText.Text = ToRetryStatus(summary);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"다시 분석하지 못했습니다: {ex.GetType().Name}";
        }
        finally
        {
            RetryLlmFailuresButton.IsEnabled = _canRetryLlmFailures && _candidates.Any(candidate => candidate.Analysis.IsTransientLlmFailureReview);
        }
    }

    private async void CandidatesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRow() is { Candidate: { } candidate })
        {
            await ExecuteCandidateAsync(candidate, _openMailAsync, "원본 메일을 열었습니다.", optimisticRemove: false);
        }
    }

    private async Task RunPrimaryAsync(object sender)
    {
        if (sender is not FrameworkElement { Tag: ReviewWorkRow row })
        {
            return;
        }

        if (row.Candidate is not null)
        {
            await ExecuteCandidateAsync(row.Candidate, _approveAsync, "등록했습니다.", optimisticRemove: true);
        }
        else if (row.ClosureSuggestion is not null)
        {
            await ExecuteClosureAsync(row.ClosureSuggestion, archive: true, "대기 항목을 보관했습니다.");
        }
    }

    private async Task RunSecondaryAsync(object sender)
    {
        if (sender is not FrameworkElement { Tag: ReviewWorkRow row })
        {
            return;
        }

        if (row.Candidate is not null)
        {
            await ExecuteCandidateAsync(row.Candidate, _ignoreAsync, "무시했습니다.", optimisticRemove: true);
        }
        else if (row.ClosureSuggestion is not null)
        {
            await ExecuteClosureAsync(row.ClosureSuggestion, archive: false, "대기 항목을 유지했습니다.");
        }
    }

    private async Task RunOpenAsync(object sender)
    {
        if (sender is FrameworkElement { Tag: ReviewWorkRow { Candidate: { } candidate } })
        {
            await ExecuteCandidateAsync(candidate, _openMailAsync, "원본 메일을 열었습니다.", optimisticRemove: false);
        }
    }

    private async Task RunCandidateAsync(object sender, Func<ReviewCandidate, Task> action, string successMessage)
    {
        if (sender is FrameworkElement { Tag: ReviewWorkRow { Candidate: { } candidate } })
        {
            await ExecuteCandidateAsync(candidate, action, successMessage, optimisticRemove: true);
        }
    }

    private async Task ExecuteCandidateAsync(ReviewCandidate candidate, Func<ReviewCandidate, Task> action, string successMessage, bool optimisticRemove)
    {
        if (!_busyRowIds.Add(candidate.Id))
        {
            StatusText.Text = "이미 처리 중인 항목입니다.";
            return;
        }

        if (optimisticRemove)
        {
            _candidates = _candidates.Where(item => item.Id != candidate.Id).ToArray();
        }

        Render();
        string? finalStatus = null;
        try
        {
            await action(candidate);
            finalStatus = successMessage;
        }
        catch (Exception ex)
        {
            finalStatus = $"처리하지 못했습니다: {ex.GetType().Name}";
        }
        finally
        {
            _busyRowIds.Remove(candidate.Id);
            Render();
            StatusText.Text = finalStatus ?? StatusText.Text;
        }
    }

    private async Task ExecuteClosureAsync(WaitingClosureSuggestion suggestion, bool archive, string successMessage)
    {
        if (!_busyRowIds.Add(suggestion.Id))
        {
            StatusText.Text = "이미 처리 중인 항목입니다.";
            return;
        }

        _closureSuggestions = _closureSuggestions.Where(item => item.Id != suggestion.Id).ToArray();
        Render();
        string? finalStatus = null;
        try
        {
            await _resolveClosureAsync(suggestion, archive);
            finalStatus = successMessage;
        }
        catch (Exception ex)
        {
            finalStatus = $"처리하지 못했습니다: {ex.GetType().Name}";
        }
        finally
        {
            _busyRowIds.Remove(suggestion.Id);
            Render();
            StatusText.Text = finalStatus ?? StatusText.Text;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Y)
        {
            e.Handled = true;
            _ = ExecuteSelectedPrimaryAsync();
        }
        else if (e.Key == Key.N)
        {
            e.Handled = true;
            _ = ExecuteSelectedClosureKeepAsync();
        }
        else if (e.Key == Key.I)
        {
            e.Handled = true;
            _ = ExecuteSelectedCandidateAsync(_ignoreAsync, "무시했습니다.");
        }
        else if (e.Key == Key.S)
        {
            e.Handled = true;
            _ = ExecuteSelectedCandidateAsync(_snoozeAsync, "내일까지 다시 표시하지 않습니다.");
        }
    }

    private ReviewWorkRow? SelectedRow()
    {
        if (CandidatesList.SelectedItem is ReviewWorkRow row)
        {
            return row;
        }

        if (CandidatesList.Items.Count == 1 && CandidatesList.Items[0] is ReviewWorkRow onlyRow)
        {
            CandidatesList.SelectedIndex = 0;
            return onlyRow;
        }

        return null;
    }

    private async Task ExecuteSelectedPrimaryAsync()
    {
        if (SelectedRow() is { } row)
        {
            if (row.Candidate is not null)
            {
                await ExecuteCandidateAsync(row.Candidate, _approveAsync, "등록했습니다.", optimisticRemove: true);
            }
            else if (row.ClosureSuggestion is not null)
            {
                await ExecuteClosureAsync(row.ClosureSuggestion, archive: true, "대기 항목을 보관했습니다.");
            }
        }
    }

    private async Task ExecuteSelectedClosureKeepAsync()
    {
        if (SelectedRow() is { ClosureSuggestion: { } suggestion })
        {
            await ExecuteClosureAsync(suggestion, archive: false, "대기 항목을 유지했습니다.");
        }
    }

    private async Task ExecuteSelectedCandidateAsync(Func<ReviewCandidate, Task> action, string successMessage)
    {
        if (SelectedRow() is { Candidate: { } candidate })
        {
            await ExecuteCandidateAsync(candidate, action, successMessage, optimisticRemove: true);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string ToRetryStatus(ReviewCandidateRetrySummary summary)
    {
        if (summary.EligibleCount == 0)
        {
            return "다시 시도할 AI 분석 항목이 없습니다.";
        }

        return $"AI 분석 {summary.EligibleCount}개 다시 시도 · 업무 {summary.TaskCreatedCount}개 · 확인 필요 {summary.ReviewCandidateCreatedCount}개"
               + (summary.MissingSourceCount > 0 ? $" · 원본 없음 {summary.MissingSourceCount}개" : string.Empty)
               + (summary.SourceLookupFailureCount > 0 ? $" · 원본 조회 실패 {summary.SourceLookupFailureCount}개" : string.Empty);
    }

    private sealed record ReviewWorkRow(
        Guid Id,
        ReviewCandidate? Candidate,
        WaitingClosureSuggestion? ClosureSuggestion,
        string Title,
        string Meta,
        string PrimaryText,
        string SecondaryText,
        Visibility SnoozeVisibility,
        bool CanOpen,
        bool CanAct)
    {
        public static ReviewWorkRow FromCandidate(ReviewCandidate candidate, DateTimeOffset now, bool isBusy) => new(
            candidate.Id,
            candidate,
            null,
            FollowUpPresentation.ActionTitle(candidate.Analysis.SuggestedTitle),
            BuildCandidateMeta(candidate, now, isBusy),
            "등록(Y)",
            "무시(I)",
            Visibility.Visible,
            !isBusy && !string.IsNullOrWhiteSpace(candidate.SourceId),
            !isBusy);

        public static ReviewWorkRow FromClosureSuggestion(WaitingClosureSuggestion suggestion, bool isBusy) => new(
            suggestion.Id,
            null,
            suggestion,
            FollowUpPresentation.ActionTitle(suggestion.TaskTitle),
            $"{suggestion.ActionText} · {suggestion.Reason}" + (isBusy ? " · 처리 중" : string.Empty),
            "보관(Y)",
            "유지(N)",
            Visibility.Collapsed,
            false,
            !isBusy);

        private static string BuildCandidateMeta(ReviewCandidate candidate, DateTimeOffset now, bool isBusy)
        {
            var parts = new List<string>
            {
                FollowUpPresentation.HumanDueText(candidate.Analysis.DueAt, now),
                FollowUpPresentation.HumanSenderText(candidate.SourceSenderDisplay, "알 수 없음")
            };
            var reason = Compact(candidate.Analysis.Summary ?? candidate.Analysis.Reason, 120);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                parts.Add(reason);
            }

            if (isBusy)
            {
                parts.Add("처리 중");
            }

            return string.Join(" · ", parts);
        }

        private static string Compact(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= maxChars ? compact : compact[..maxChars].TrimEnd() + "…";
        }
    }
}
