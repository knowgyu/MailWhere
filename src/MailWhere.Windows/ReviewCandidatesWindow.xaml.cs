using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;

namespace MailWhere.Windows;

public partial class ReviewCandidatesWindow : Window
{
    private IReadOnlyList<ReviewCandidate> _candidates;
    private readonly Func<ReviewCandidate, Task> _approveAsync;
    private readonly Func<ReviewCandidate, Task> _openMailAsync;
    private readonly Func<ReviewCandidate, Task> _snoozeAsync;
    private readonly Func<ReviewCandidate, Task> _ignoreAsync;

    public ReviewCandidatesWindow(
        IReadOnlyList<ReviewCandidate> candidates,
        Func<ReviewCandidate, Task> approveAsync,
        Func<ReviewCandidate, Task> openMailAsync,
        Func<ReviewCandidate, Task> snoozeAsync,
        Func<ReviewCandidate, Task> ignoreAsync)
    {
        InitializeComponent();
        _candidates = candidates;
        _approveAsync = approveAsync;
        _openMailAsync = openMailAsync;
        _snoozeAsync = snoozeAsync;
        _ignoreAsync = ignoreAsync;
        Render();
    }

    public void Refresh(IReadOnlyList<ReviewCandidate> candidates)
    {
        _candidates = candidates;
        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var rows = _candidates
            .Select(candidate => ReviewCandidateRow.FromCandidate(candidate, now))
            .ToArray();
        CandidatesList.ItemsSource = rows;
        CandidatesList.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = rows.Length == 0 ? "표시할 검토 후보가 없습니다." : $"검토 후보 {rows.Length}개";
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _approveAsync, "등록했습니다.");
    private async void OpenMail_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _openMailAsync, "원본 메일을 열었습니다.");
    private async void Snooze_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _snoozeAsync, "내일까지 다시 표시하지 않습니다.");
    private async void Ignore_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _ignoreAsync, "무시했습니다.");

    private async void CandidatesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CandidatesList.SelectedItem is ReviewCandidateRow row)
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
        try
        {
            await action(candidate);
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"처리하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record ReviewCandidateRow(ReviewCandidate Candidate, string Title, string Meta, bool CanOpen)
    {
        public static ReviewCandidateRow FromCandidate(ReviewCandidate candidate, DateTimeOffset now) => new(
            candidate,
            FollowUpPresentation.ActionTitle(candidate.Analysis.SuggestedTitle),
            $"{FollowUpPresentation.HumanDueText(candidate.Analysis.DueAt, now)} · {FollowUpPresentation.HumanSenderText(candidate.SourceSenderDisplay, "알 수 없음")}",
            !string.IsNullOrWhiteSpace(candidate.SourceId));
    }
}
