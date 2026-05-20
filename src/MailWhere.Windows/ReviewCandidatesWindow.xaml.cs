using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;
using MailWhere.Core.Pipeline;

namespace MailWhere.Windows;

public partial class ReviewCandidatesWindow : Window
{
    private IReadOnlyList<ReviewCandidate> _candidates;
    private readonly Func<ReviewCandidate, Task> _approveAsync;
    private readonly Func<ReviewCandidate, Task> _openMailAsync;
    private readonly Func<ReviewCandidate, Task> _snoozeAsync;
    private readonly Func<ReviewCandidate, Task> _ignoreAsync;
    private readonly Func<Task<ReviewCandidateRetrySummary>> _retryLlmFailuresAsync;
    private readonly HashSet<Guid> _busyCandidateIds = new();
    private bool _canRetryLlmFailures;

    public ReviewCandidatesWindow(
        IReadOnlyList<ReviewCandidate> candidates,
        Func<ReviewCandidate, Task> approveAsync,
        Func<ReviewCandidate, Task> openMailAsync,
        Func<ReviewCandidate, Task> snoozeAsync,
        Func<ReviewCandidate, Task> ignoreAsync,
        Func<Task<ReviewCandidateRetrySummary>> retryLlmFailuresAsync,
        bool canRetryLlmFailures)
    {
        InitializeComponent();
        _candidates = candidates;
        _approveAsync = approveAsync;
        _openMailAsync = openMailAsync;
        _snoozeAsync = snoozeAsync;
        _ignoreAsync = ignoreAsync;
        _retryLlmFailuresAsync = retryLlmFailuresAsync;
        _canRetryLlmFailures = canRetryLlmFailures;
        Render();
    }

    public void Refresh(IReadOnlyList<ReviewCandidate> candidates, bool? canRetryLlmFailures = null)
    {
        _candidates = candidates;
        if (canRetryLlmFailures is not null)
        {
            _canRetryLlmFailures = canRetryLlmFailures.Value;
        }

        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var rows = _candidates
            .Select(candidate => ReviewCandidateRow.FromCandidate(candidate, now, _busyCandidateIds.Contains(candidate.Id)))
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
        StatusText.Text = rows.Length == 0
            ? "표시할 확인 필요 항목이 없습니다."
            : hasRetryableFailure && _canRetryLlmFailures
                ? $"확인 필요 {rows.Length}개 · 실패한 AI 분석을 다시 시도할 수 있습니다"
                : hasRetryableFailure
                    ? $"확인 필요 {rows.Length}개 · AI 설정이 꺼져 다시 시도할 수 없습니다"
                    : $"확인 필요 {rows.Length}개 · AI 실패 항목이 없어 다시 시도는 비활성화됩니다";
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _approveAsync, "등록했습니다.");
    private async void OpenMail_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _openMailAsync, "원본 메일을 열었습니다.");
    private async void Snooze_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _snoozeAsync, "내일까지 다시 표시하지 않습니다.");
    private async void Ignore_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _ignoreAsync, "무시했습니다.");

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
        if (SelectedRow() is { } row)
        {
            await ExecuteAsync(row.Candidate, _openMailAsync, "원본 메일을 열었습니다.");
        }
    }

    private async Task RunAsync(object sender, Func<ReviewCandidate, Task> action, string successMessage)
    {
        if (sender is FrameworkElement { Tag: ReviewCandidateRow row })
        {
            await ExecuteAsync(row.Candidate, action, successMessage);
        }
    }

    private async Task ExecuteAsync(ReviewCandidate candidate, Func<ReviewCandidate, Task> action, string successMessage)
    {
        if (!_busyCandidateIds.Add(candidate.Id))
        {
            StatusText.Text = "이미 처리 중인 항목입니다.";
            return;
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
            _busyCandidateIds.Remove(candidate.Id);
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
            _ = ExecuteSelectedAsync(_approveAsync, "등록했습니다.");
        }
        else if (e.Key == Key.I)
        {
            e.Handled = true;
            _ = ExecuteSelectedAsync(_ignoreAsync, "무시했습니다.");
        }
        else if (e.Key == Key.S)
        {
            e.Handled = true;
            _ = ExecuteSelectedAsync(_snoozeAsync, "내일까지 다시 표시하지 않습니다.");
        }
    }

    private ReviewCandidateRow? SelectedRow()
    {
        if (CandidatesList.SelectedItem is ReviewCandidateRow row)
        {
            return row;
        }

        if (CandidatesList.Items.Count == 1 && CandidatesList.Items[0] is ReviewCandidateRow onlyRow)
        {
            CandidatesList.SelectedIndex = 0;
            return onlyRow;
        }

        return null;
    }

    private async Task ExecuteSelectedAsync(Func<ReviewCandidate, Task> action, string successMessage)
    {
        if (SelectedRow() is { } row)
        {
            await ExecuteAsync(row.Candidate, action, successMessage);
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

    private sealed record ReviewCandidateRow(ReviewCandidate Candidate, string Title, string Meta, bool CanOpen, bool CanAct)
    {
        public static ReviewCandidateRow FromCandidate(ReviewCandidate candidate, DateTimeOffset now, bool isBusy) => new(
            candidate,
            FollowUpPresentation.ActionTitle(candidate.Analysis.SuggestedTitle),
            $"{FollowUpPresentation.HumanDueText(candidate.Analysis.DueAt, now)} · {FollowUpPresentation.HumanSenderText(candidate.SourceSenderDisplay, "알 수 없음")}" + (isBusy ? " · 처리 중" : string.Empty),
            !isBusy && !string.IsNullOrWhiteSpace(candidate.SourceId),
            !isBusy);
    }
}
