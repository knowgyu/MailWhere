using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MailWhere.Core.Domain;
using MailWhere.Core.Reminders;
using MailWhere.Core.Scheduling;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace MailWhere.Windows;

public partial class DailyBoardWindow : Window
{
    private readonly List<LocalTaskItem> _tasks;
    private readonly List<ReviewCandidate> _candidates;
    private readonly Func<LocalTaskItem, Task> _openTaskAsync;
    private readonly Func<LocalTaskItem, Task<bool>> _archiveTaskAsync;
    private readonly Func<LocalTaskItem, DateTimeOffset, Task<bool>> _snoozeTaskAsync;
    private readonly Func<LocalTaskItem, DateTimeOffset, Task<bool>> _setTaskDueAsync;
    private readonly Func<LocalTaskItem, TaskEditRequest, Task<LocalTaskItem?>> _updateTaskAsync;
    private readonly Func<string, DateTimeOffset?, Task<LocalTaskItem?>> _addTaskAsync;
    private readonly Func<Task> _openReviewCandidatesAsync;
    private DateTimeOffset _now;
    private string? _statusMessage;
    private DailyBoardOpenOptions _options;
    private BoardRouteFilter _filter;

    public DailyBoardWindow(
        IReadOnlyList<LocalTaskItem> tasks,
        IReadOnlyList<ReviewCandidate> candidates,
        DateTimeOffset now,
        string dailyBoardTime,
        DailyBoardOpenOptions options,
        Func<LocalTaskItem, Task> openTaskAsync,
        Func<LocalTaskItem, Task<bool>> archiveTaskAsync,
        Func<LocalTaskItem, DateTimeOffset, Task<bool>> snoozeTaskAsync,
        Func<LocalTaskItem, DateTimeOffset, Task<bool>> setTaskDueAsync,
        Func<LocalTaskItem, TaskEditRequest, Task<LocalTaskItem?>> updateTaskAsync,
        Func<string, DateTimeOffset?, Task<LocalTaskItem?>> addTaskAsync,
        Func<Task> openReviewCandidatesAsync)
    {
        InitializeComponent();
        _ = dailyBoardTime;
        _tasks = tasks.ToList();
        _candidates = candidates.ToList();
        _now = now;
        _options = options;
        _filter = options.Filter;
        _openTaskAsync = openTaskAsync;
        _archiveTaskAsync = archiveTaskAsync;
        _snoozeTaskAsync = snoozeTaskAsync;
        _setTaskDueAsync = setTaskDueAsync;
        _updateTaskAsync = updateTaskAsync;
        _addTaskAsync = addTaskAsync;
        _openReviewCandidatesAsync = openReviewCandidatesAsync;

        Render();
    }

    public void ApplyOpenOptions(
        DailyBoardOpenOptions options,
        DateTimeOffset now,
        string dailyBoardTime,
        IReadOnlyList<LocalTaskItem> tasks,
        IReadOnlyList<ReviewCandidate> candidates)
    {
        _ = dailyBoardTime;
        _options = options;
        _filter = options.Filter;
        _now = now;
        _tasks.Clear();
        _tasks.AddRange(tasks);
        _candidates.Clear();
        _candidates.AddRange(candidates);
        Render();
    }

    private void Render()
    {
        _now = DateTimeOffset.Now;
        var (items, hiddenCandidateCount) = BuildBoardItems();
        var brief = DailyBriefPlanner.Build(_tasks, _candidates, _now);

        TitleText.Text = "업무 보드";
        SubtitleText.Text = _options.ShowBriefSummary
            ? "오늘 볼 업무만 한 줄씩 정리합니다."
            : "메일에서 만든 업무를 한 목록으로 봅니다.";
        UpdatedAtText.Text = $"{_now:MM/dd HH:mm} 기준";
        SummaryText.Text = $"{FilterLabel(_filter)} · 업무 {items.Length}";
        BriefSummaryPanel.Visibility = _options.ShowBriefSummary ? Visibility.Visible : Visibility.Collapsed;
        BriefSummaryBodyText.Text = brief.TotalHighlights == 0
            ? "오늘 바로 볼 업무는 없습니다."
            : $"오늘 볼 업무 {brief.TotalHighlights}개";
        BriefSummaryMetaText.Text = brief.HiddenCandidateCount > 0
            ? $"검토 후보 {brief.HiddenCandidateCount}개"
            : string.Empty;
        ReviewCandidatesButton.Visibility = hiddenCandidateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewCandidatesButton.Content = $"검토 후보 {hiddenCandidateCount}개 보기";
        BriefReviewCandidatesButton.Visibility = hiddenCandidateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        BriefReviewCandidatesButton.Content = $"검토 후보 {hiddenCandidateCount}개 보기";
        BriefSummaryMetaText.Visibility = string.IsNullOrWhiteSpace(BriefSummaryMetaText.Text) ? Visibility.Collapsed : Visibility.Visible;
        FillList(TaskList, items);
        var hasVisibleItems = items.Length > 0;
        BoardListPanel.Visibility = hasVisibleItems ? Visibility.Visible : Visibility.Collapsed;
        BoardEmptyText.Visibility = hasVisibleItems ? Visibility.Collapsed : Visibility.Visible;
        BoardEmptyText.Text = "표시할 업무가 없습니다.";
        UpdateStatusText();
        HighlightFilter();
    }

    private (LocalTaskItem[] Items, int HiddenCandidateCount) BuildBoardItems()
    {
        var items = DailyBoardRouteTaskSelector.SelectVisibleTasks(
                _tasks,
                _candidates,
                _now,
                _filter,
                _options.ShowBriefSummary)
            .OrderBy(SortKey)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        var candidateCount = _candidates.Count(candidate => !candidate.Suppressed && (candidate.SnoozeUntil is null || candidate.SnoozeUntil <= _now));
        return (items, candidateCount);
    }

    private void FillList(WpfListBox list, IReadOnlyList<LocalTaskItem> tasks) =>
        list.ItemsSource = tasks.Select(task => BoardCardItem.FromTask(task, _now)).ToArray();

    private static DateTimeOffset SortKey(LocalTaskItem task) =>
        task.DueAt ?? task.SnoozeUntil ?? DateTimeOffset.MaxValue;

    private static string FilterLabel(BoardRouteFilter filter) => filter switch
    {
        BoardRouteFilter.All => "전체",
        BoardRouteFilter.Today => "오늘",
        BoardRouteFilter.Week => "7일 내",
        BoardRouteFilter.Month => "30일 내",
        BoardRouteFilter.NoDue => "기한 미정",
        _ => "전체"
    };

    private void HighlightFilter()
    {
        SetFilterStyle(AllFilterButton, _filter == BoardRouteFilter.All);
        SetFilterStyle(TodayFilterButton, _filter == BoardRouteFilter.Today);
        SetFilterStyle(WeekFilterButton, _filter == BoardRouteFilter.Week);
        SetFilterStyle(MonthFilterButton, _filter == BoardRouteFilter.Month);
        SetFilterStyle(NoDueFilterButton, _filter == BoardRouteFilter.NoDue);
    }

    private static void SetFilterStyle(System.Windows.Controls.Button button, bool active)
    {
        button.Background = active ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEE, 0xF3, 0xFF));
        button.BorderBrush = active ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x58, 0xF2)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD8, 0xE2, 0xFF));
    }

    private void AllFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardRouteFilter.All);
    private void TodayFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardRouteFilter.Today);
    private void WeekFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardRouteFilter.Week);
    private void MonthFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardRouteFilter.Month);
    private void NoDueFilter_Click(object sender, RoutedEventArgs e) => SetFilter(BoardRouteFilter.NoDue);

    private void SetFilter(BoardRouteFilter filter)
    {
        _filter = filter;
        _options = _options with
        {
            Filter = filter,
            ShowBriefSummary = filter == BoardRouteFilter.Today && _options.Origin != BoardOrigin.Manual
        };
        Render();
    }

    private async void OpenTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is { Task: { } task })
            {
                await _openTaskAsync(task);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"원본 메일을 열지 못했습니다: {ex.GetType().Name}");
        }
    }

    private async void ArchiveTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            if (!await _archiveTaskAsync(task))
            {
                SetStatus("이미 처리된 항목입니다.");
                return;
            }

            _tasks.RemoveAll(item => item.Id == task.Id);
            SetStatus("보관했습니다. 이 목록에는 다시 표시되지 않습니다.");
            Render();
        }
        catch (Exception ex)
        {
            SetStatus($"보관에 실패했습니다: {ex.GetType().Name}");
        }
    }

    private async Task SnoozeCustomTaskAsync(LocalTaskItem task)
    {
        var dialog = new DueDateDialog(DateTime.Today, task.SnoozeUntil?.DateTime ?? task.DueAt?.DateTime)
        {
            Owner = this,
            Title = "나중에 보기"
        };
        if (dialog.ShowDialog() != true || dialog.SelectedDueAt is not { } until)
        {
            return;
        }

        await SnoozeTaskAsync(task, until, $"{until:MM/dd}에 다시 표시합니다.");
    }

    private void MoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || GetCard(sender) is not { Task: { } task })
        {
            return;
        }

        var menu = new WpfContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        AddMenuItem(menu, "오늘 1시에 다시 보기", () => SnoozeTaskAsync(task, SnoozePlanner.Plan(SnoozePreset.TodayAtOnePm, DateTimeOffset.Now), "오후 1시에 다시 표시합니다."));
        AddMenuItem(menu, "내일 아침 다시 보기", () => SnoozeTaskAsync(task, SnoozePlanner.Plan(SnoozePreset.TomorrowMorning, DateTimeOffset.Now), "내일 아침 다시 표시합니다."));
        AddMenuItem(menu, "다음 월요일 다시 보기", () => SnoozeTaskAsync(task, SnoozePlanner.Plan(SnoozePreset.NextMondayMorning, DateTimeOffset.Now), "다음 주 월요일 다시 표시합니다."));
        AddMenuItem(menu, "직접 날짜 선택", () => SnoozeCustomTaskAsync(task));
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void AddMenuItem(WpfContextMenu menu, string header, Func<Task> action)
    {
        var item = new WpfMenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus($"작업을 처리하지 못했습니다: {ex.GetType().Name}");
            }
        };
        menu.Items.Add(item);
    }

    private async Task SnoozeTaskAsync(LocalTaskItem task, DateTimeOffset until, string message)
    {
        var now = DateTimeOffset.Now;
        var effectiveUntil = until <= now ? now.AddHours(1) : until;
        if (!await _snoozeTaskAsync(task, effectiveUntil))
        {
            SetStatus("이미 처리된 항목이라 나중에 보기로 바꾸지 못했습니다.");
            return;
        }

        var index = _tasks.FindIndex(item => item.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task with { Status = LocalTaskStatus.Snoozed, SnoozeUntil = effectiveUntil, UpdatedAt = DateTimeOffset.UtcNow };
        }

        SetStatus(effectiveUntil == until ? message : $"{effectiveUntil:MM/dd HH:mm}에 다시 표시합니다.");
        Render();
    }

    private async void BoardCard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is WpfListBox { SelectedItem: BoardCardItem { Task: { } task } })
            {
                await EditTaskAsync(task);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"업무 수정에 실패했습니다: {ex.GetType().Name}");
        }
    }

    private async Task EditTaskAsync(LocalTaskItem task)
    {
        var dialog = new TaskEditDialog(task)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.EditRequest is not { } edit)
        {
            return;
        }

        var updated = await _updateTaskAsync(task, edit);
        if (updated is null)
        {
            SetStatus("이미 처리된 항목이라 수정하지 못했습니다.");
            return;
        }

        ReplaceTask(updated);
        SetStatus("업무 내용을 수정했습니다.");
        Render();
    }

    private async void SetDue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            await SetDueForTaskAsync(task);
        }
        catch (Exception ex)
        {
            SetStatus($"기한 설정에 실패했습니다: {ex.GetType().Name}");
        }
    }

    private async Task SetDueForTaskAsync(LocalTaskItem task)
    {
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
            SetStatus("이미 처리된 항목이라 기한을 바꾸지 못했습니다.");
            return;
        }

        var index = _tasks.FindIndex(item => item.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task with { DueAt = dueAt, Status = task.Status == LocalTaskStatus.Snoozed ? LocalTaskStatus.Open : task.Status, SnoozeUntil = null, UpdatedAt = DateTimeOffset.UtcNow };
        }

        SetStatus($"기한을 {dueAt:MM/dd}로 설정했습니다.");
        Render();
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
                SetStatus("직접 추가에 실패했습니다.");
                return;
            }

            _tasks.Add(created);
            SetStatus("직접 추가한 할 일을 보드에 넣었습니다.");
            Render();
        }
        catch (Exception ex)
        {
            SetStatus($"직접 추가에 실패했습니다: {ex.GetType().Name}");
        }
    }

    private async void ReviewCandidates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _openReviewCandidatesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"검토 후보를 열지 못했습니다: {ex.GetType().Name}");
        }
    }

    private static BoardCardItem? GetCard(object sender) =>
        sender is FrameworkElement { Tag: BoardCardItem item } ? item : null;

    private void ReplaceTask(LocalTaskItem updated)
    {
        var index = _tasks.FindIndex(item => item.Id == updated.Id);
        if (index >= 0)
        {
            _tasks[index] = updated;
        }
    }

    private void DailyBoardWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        BoardStatusText.Text = _statusMessage ?? string.Empty;
        BoardStatusText.Visibility = string.IsNullOrWhiteSpace(_statusMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class BoardCardItem
    {
        private BoardCardItem(LocalTaskItem? task, string title, string meta, string dueText)
        {
            Task = task;
            Title = title;
            Meta = meta;
            DueText = dueText;
        }

        public LocalTaskItem? Task { get; }
        public string Title { get; }
        public string Meta { get; }
        public bool HasTask => Task is not null;
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task?.SourceId);
        public string DueText { get; }
        public Visibility DueButtonVisibility => Task is null ? Visibility.Collapsed : Visibility.Visible;

        public static BoardCardItem FromTask(LocalTaskItem task, DateTimeOffset now)
        {
            var sender = string.IsNullOrWhiteSpace(task.SourceSenderDisplay) ? "직접 추가" : CompactLine(task.SourceSenderDisplay, 18);
            var received = task.SourceReceivedAt ?? task.CreatedAt;
            var snooze = task.Status == LocalTaskStatus.Snoozed && task.SnoozeUntil is not null
                ? $"나중에 {FormatDate(task.SnoozeUntil.Value, now)} · "
                : string.Empty;
            return new BoardCardItem(
                task,
                CompactLine(FollowUpPresentation.ActionTitle(task.Title), 120),
                $"{snooze}{sender} · {received:MM/dd HH:mm}",
                task.DueAt is null ? "기한 추가" : FormatDate(task.DueAt.Value, now));
        }

        private static string FormatDate(DateTimeOffset value, DateTimeOffset now)
        {
            var date = value.LocalDateTime.Date;
            var today = now.LocalDateTime.Date;
            if (date == today)
            {
                return $"오늘 {value:HH:mm}";
            }

            if (date == today.AddDays(1))
            {
                return $"내일 {value:HH:mm}";
            }

            return $"{DdayFormatter.Format(value, now)} · {value:MM/dd HH:mm}";
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
