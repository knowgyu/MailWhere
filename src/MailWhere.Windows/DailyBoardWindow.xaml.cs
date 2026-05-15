using System.Windows;
using MailWhere.Core.Domain;
using MailWhere.Core.Reminders;

namespace MailWhere.Windows;

public partial class DailyBoardWindow : Window
{
    private readonly List<LocalTaskItem> _tasks;
    private readonly Func<LocalTaskItem, Task> _openTaskAsync;
    private readonly Func<LocalTaskItem, Task<bool>> _dismissTaskAsync;
    private readonly Func<LocalTaskItem, DateTimeOffset, Task<bool>> _setTaskDueAsync;
    private readonly Func<string, DateTimeOffset?, Task<LocalTaskItem?>> _addTaskAsync;
    private DateTimeOffset _now;
    private BoardFilter _filter = BoardFilter.Today;

    public DailyBoardWindow(
        IReadOnlyList<LocalTaskItem> tasks,
        DateTimeOffset now,
        string dailyBoardTime,
        Func<LocalTaskItem, Task> openTaskAsync,
        Func<LocalTaskItem, Task<bool>> dismissTaskAsync,
        Func<LocalTaskItem, DateTimeOffset, Task<bool>> setTaskDueAsync,
        Func<string, DateTimeOffset?, Task<LocalTaskItem?>> addTaskAsync)
    {
        InitializeComponent();
        _tasks = tasks.ToList();
        _now = now;
        _openTaskAsync = openTaskAsync;
        _dismissTaskAsync = dismissTaskAsync;
        _setTaskDueAsync = setTaskDueAsync;
        _addTaskAsync = addTaskAsync;
        SubtitleText.Text = $"{now:yyyy-MM-dd HH:mm} 기준 · 기본 보드 시간 {dailyBoardTime}";
        Render();
    }

    private void Render()
    {
        _now = DateTimeOffset.Now;
        var visible = FilterTasks(_tasks, _now, _filter).ToArray();
        var actions = visible.Where(task => !IsSchedule(task)).OrderBy(SortKey).ThenBy(task => task.CreatedAt).ToArray();
        var schedules = visible.Where(IsSchedule).OrderBy(SortKey).ThenBy(task => task.CreatedAt).ToArray();

        SummaryText.Text = $"{FilterLabel(_filter)} · 할 일 {actions.Length} · 일정 {schedules.Length}";
        FillList(ActionList, actions, "표시할 할 일이 없습니다.");
        FillList(ScheduleList, schedules, "표시할 일정이 없습니다.");
        HighlightFilter();
    }

    private void FillList(System.Windows.Controls.ListBox list, IReadOnlyList<LocalTaskItem> tasks, string emptyText)
    {
        list.ItemsSource = tasks.Count == 0
            ? new[] { BoardCardItem.Empty(emptyText) }
            : tasks.Select(task => BoardCardItem.FromTask(task, _now)).ToArray();
    }

    private static IEnumerable<LocalTaskItem> FilterTasks(IEnumerable<LocalTaskItem> tasks, DateTimeOffset now, BoardFilter filter) =>
        filter switch
        {
            BoardFilter.Today => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.Date <= now.Date),
            BoardFilter.Week => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.Date <= now.AddDays(7).Date),
            BoardFilter.Month => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.Date <= now.AddDays(30).Date),
            BoardFilter.NoDue => tasks.Where(task => task.DueAt is null),
            _ => tasks
        };

    private static bool IsSchedule(LocalTaskItem task) =>
        task.Kind is FollowUpKind.Meeting or FollowUpKind.CalendarEvent;

    private static DateTimeOffset SortKey(LocalTaskItem task) =>
        task.DueAt ?? DateTimeOffset.MaxValue;

    private static string FilterLabel(BoardFilter filter) => filter switch
    {
        BoardFilter.Today => "오늘",
        BoardFilter.Week => "7일 내",
        BoardFilter.Month => "30일 내",
        BoardFilter.NoDue => "기한 미정",
        _ => "전체"
    };

    private void HighlightFilter()
    {
        SetFilterStyle(TodayFilterButton, _filter == BoardFilter.Today);
        SetFilterStyle(WeekFilterButton, _filter == BoardFilter.Week);
        SetFilterStyle(MonthFilterButton, _filter == BoardFilter.Month);
        SetFilterStyle(NoDueFilterButton, _filter == BoardFilter.NoDue);
    }

    private static void SetFilterStyle(System.Windows.Controls.Button button, bool active)
    {
        button.Background = active ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEE, 0xF3, 0xFF));
        button.BorderBrush = active ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x58, 0xF2)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD8, 0xE2, 0xFF));
    }

    private void TodayFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardFilter.Today);
    private void WeekFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardFilter.Week);
    private void MonthFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardFilter.Month);
    private void NoDueFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardFilter.NoDue);

    private void SetFilter(BoardFilter filter)
    {
        _filter = filter;
        Render();
    }

    private async void OpenTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            await _openTaskAsync(task);
        }
        catch (Exception ex)
        {
            FooterText.Text = $"원본 메일을 열지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void DismissTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            if (!await _dismissTaskAsync(task))
            {
                FooterText.Text = "이미 처리된 항목입니다.";
                return;
            }

            _tasks.RemoveAll(item => item.Id == task.Id);
            FooterText.Text = "업무보드에서 삭제했습니다. Outlook 원본 메일은 그대로 유지됩니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"업무보드 삭제에 실패했습니다: {ex.GetType().Name}";
        }
    }

    private async void SetDue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            var dialog = new DueDateDialog(DateTime.Today, task.DueAt?.DateTime)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.SelectedDueAt is not { } dueAt)
            {
                return;
            }

            if (!await _setTaskDueAsync(task, dueAt))
            {
                FooterText.Text = "이미 처리된 항목이라 기한을 바꾸지 못했습니다.";
                return;
            }

            var index = _tasks.FindIndex(item => item.Id == task.Id);
            if (index >= 0)
            {
                _tasks[index] = task with { DueAt = dueAt, UpdatedAt = DateTimeOffset.UtcNow };
            }

            FooterText.Text = $"기한을 {dueAt:MM/dd}로 설정했습니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"기한 설정에 실패했습니다: {ex.GetType().Name}";
        }
    }

    private async void AddManualTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ManualTaskDialog(DateTime.Today)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var created = await _addTaskAsync(dialog.TaskTitle, dialog.DueAt);
            if (created is null)
            {
                FooterText.Text = "직접 추가에 실패했습니다.";
                return;
            }

            _tasks.Add(created);
            FooterText.Text = "직접 추가한 할 일을 보드에 넣었습니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"직접 추가에 실패했습니다: {ex.GetType().Name}";
        }
    }

    private static BoardCardItem? GetCard(object sender) =>
        sender is FrameworkElement { Tag: BoardCardItem item } ? item : null;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private enum BoardFilter
    {
        Today,
        Week,
        Month,
        NoDue
    }

    private sealed class BoardCardItem
    {
        private BoardCardItem(LocalTaskItem? task, string title, string meta)
        {
            Task = task;
            Title = title;
            Meta = meta;
        }

        public LocalTaskItem? Task { get; }
        public string Title { get; }
        public string Meta { get; }
        public bool HasTask => Task is not null;
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task?.SourceId);
        public string DueButtonText => Task?.DueAt is null ? "기한 설정" : "기한 변경";
        public Visibility DueButtonVisibility => Task is null ? Visibility.Collapsed : Visibility.Visible;

        public static BoardCardItem Empty(string message) => new(null, message, string.Empty);

        public static BoardCardItem FromTask(LocalTaskItem task, DateTimeOffset now)
        {
            var due = task.DueAt is null ? "기한 미정" : DdayFormatter.Format(task.DueAt.Value, now);
            var sender = string.IsNullOrWhiteSpace(task.SourceSenderDisplay) ? "직접 추가" : CompactLine(task.SourceSenderDisplay, 18);
            var received = task.SourceReceivedAt ?? task.CreatedAt;
            return new BoardCardItem(task, CompactLine(task.Title, 44), $"{due} | {sender} | {received:MM/dd HH:mm}");
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
    }
}
