using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;
using MailWhere.Core.Reminders;
using MailWhere.OutlookCom;

namespace MailWhere.Windows;

public partial class DailyBoardWindow : Window
{
    public DailyBoardWindow(IReadOnlyList<LocalTaskItem> tasks, IReadOnlyList<ReviewCandidate> reviewCandidates, DateTimeOffset now, string dailyBoardTime)
    {
        InitializeComponent();
        SubtitleText.Text = $"{now:yyyy-MM-dd HH:mm} 기준 · 기본 보드 시간 {dailyBoardTime}";
        Populate(tasks, reviewCandidates, now);
    }

    private void Populate(IReadOnlyList<LocalTaskItem> tasks, IReadOnlyList<ReviewCandidate> reviewCandidates, DateTimeOffset now)
    {
        var dueToday = tasks
            .Where(task => task.DueAt is not null && task.DueAt.Value.Date <= now.Date)
            .OrderBy(task => task.DueAt)
            .ToArray();
        var upcoming = tasks
            .Where(task => task.DueAt is not null && task.DueAt.Value.Date > now.Date && task.DueAt.Value <= now.AddDays(7))
            .OrderBy(task => task.DueAt)
            .ToArray();
        var noDue = tasks
            .Where(task => task.DueAt is null)
            .OrderByDescending(task => task.CreatedAt)
            .Take(10)
            .ToArray();

        SummaryText.Text = $"오늘/지남 {dueToday.Length} · 7일 내 {upcoming.Length} · 확인 필요 {reviewCandidates.Count}";
        FillTaskList(DueTodayList, dueToday, now, "오늘 마감/지난 할 일이 없습니다.");
        FillTaskList(UpcomingList, upcoming, now, "앞으로 7일 내 마감이 없습니다.");
        FillTaskList(NoDueList, noDue, now, "마감 없는 할 일이 없습니다.");
        FillReviewSummary(reviewCandidates.Count);
    }

    private static void FillTaskList(System.Windows.Controls.ListBox list, IReadOnlyList<LocalTaskItem> tasks, DateTimeOffset now, string emptyText)
    {
        list.Items.Clear();
        foreach (var task in tasks)
        {
            var due = task.DueAt is null ? "마감 없음" : $"{DdayFormatter.Format(task.DueAt.Value, now)} · {task.DueAt.Value:MM/dd HH:mm}";
            var display = $"{due}\n{CompactLine(task.Title, 54)}"
                          + (string.IsNullOrWhiteSpace(task.SourceId) ? string.Empty : "\n더블클릭: Outlook 원본 메일");
            list.Items.Add(new BoardItem(display, task.SourceId));
        }

        if (tasks.Count == 0)
        {
            list.Items.Add(emptyText);
        }
    }

    private void FillReviewSummary(int candidateCount)
    {
        ReviewSummaryTitle.Text = $"확인 필요한 항목 {candidateCount}개";
        ReviewSummaryText.Text = candidateCount == 0
            ? "자동 등록하기 애매한 메일이 없습니다."
            : "자동 등록하기 애매한 메일입니다. 검토함에서 한 번에 등록하거나 무시하세요.";
        OpenReviewButton.IsEnabled = candidateCount > 0;
        OpenReviewButton.Content = candidateCount == 0 ? "검토할 항목 없음" : "검토하기";
    }

    private void OpenReview_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.OpenReviewTab();
        }

        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OpenSelectedMail_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list || list.SelectedItem is not BoardItem item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.SourceId))
        {
            SubtitleText.Text = "이 항목은 원본 메일 연결 정보가 없습니다.";
            return;
        }

        var result = await new OutlookComMailOpener().OpenAsync(item.SourceId);
        SubtitleText.Text = result.Success ? result.Message : $"원본 메일 열기 실패: {result.Message}";
    }

    private static string CompactLine(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxChars ? compact : compact[..maxChars].TrimEnd() + "…";
    }

    private sealed record BoardItem(string Display, string? SourceId)
    {
        public override string ToString() => Display;
    }
}
