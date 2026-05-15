using System.Windows;
using MailWhere.Core.Domain;
using MailWhere.Core.Localization;
using MailWhere.Core.Reminders;

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
        FillReviewList(reviewCandidates);
    }

    private static void FillTaskList(System.Windows.Controls.ListBox list, IReadOnlyList<LocalTaskItem> tasks, DateTimeOffset now, string emptyText)
    {
        list.Items.Clear();
        foreach (var task in tasks)
        {
            var due = task.DueAt is null ? "마감 없음" : $"{DdayFormatter.Format(task.DueAt.Value, now)} · {task.DueAt.Value:MM/dd HH:mm}";
            list.Items.Add($"{due}  |  {task.Title}  |  {task.Reason}");
        }

        if (tasks.Count == 0)
        {
            list.Items.Add(emptyText);
        }
    }

    private void FillReviewList(IReadOnlyList<ReviewCandidate> candidates)
    {
        ReviewList.Items.Clear();
        foreach (var candidate in candidates.Take(10))
        {
            var due = candidate.Analysis.DueAt is null
                ? "마감 불명"
                : $"{DdayFormatter.Format(candidate.Analysis.DueAt.Value, DateTimeOffset.Now)} · {candidate.Analysis.DueAt.Value:MM/dd HH:mm}";
            ReviewList.Items.Add($"{KoreanLabels.Kind(candidate.Analysis.Kind)} · {candidate.Analysis.Confidence:P0} · {due}  |  {candidate.Analysis.SuggestedTitle}");
        }

        if (candidates.Count == 0)
        {
            ReviewList.Items.Add("확인 필요한 후보가 없습니다.");
        }
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
}
