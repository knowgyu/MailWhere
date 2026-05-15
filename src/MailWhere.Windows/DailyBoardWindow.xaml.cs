using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MailWhere.Core.Domain;
using MailWhere.Core.Reminders;
using MailWhere.Core.Scheduling;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace MailWhere.Windows;

public partial class DailyBoardWindow : Window
{
    private readonly List<LocalTaskItem> _tasks;
    private readonly List<ReviewCandidate> _candidates;
    private readonly Func<LocalTaskItem, Task> _openTaskAsync;
    private readonly Func<LocalTaskItem, Task<bool>> _completeTaskAsync;
    private readonly Func<LocalTaskItem, Task<bool>> _dismissTaskAsync;
    private readonly Func<LocalTaskItem, DateTimeOffset, Task<bool>> _snoozeTaskAsync;
    private readonly Func<LocalTaskItem, DateTimeOffset, Task<bool>> _setTaskDueAsync;
    private readonly Func<string, DateTimeOffset?, Task<LocalTaskItem?>> _addTaskAsync;
    private readonly Func<Task> _openReviewCandidatesAsync;
    private DateTimeOffset _now;
    private string _dailyBoardTime;
    private DailyBoardOpenOptions _options;
    private BoardRouteFilter _filter;

    public DailyBoardWindow(
        IReadOnlyList<LocalTaskItem> tasks,
        IReadOnlyList<ReviewCandidate> candidates,
        DateTimeOffset now,
        string dailyBoardTime,
        DailyBoardOpenOptions options,
        Func<LocalTaskItem, Task> openTaskAsync,
        Func<LocalTaskItem, Task<bool>> completeTaskAsync,
        Func<LocalTaskItem, Task<bool>> dismissTaskAsync,
        Func<LocalTaskItem, DateTimeOffset, Task<bool>> snoozeTaskAsync,
        Func<LocalTaskItem, DateTimeOffset, Task<bool>> setTaskDueAsync,
        Func<string, DateTimeOffset?, Task<LocalTaskItem?>> addTaskAsync,
        Func<Task> openReviewCandidatesAsync)
    {
        InitializeComponent();
        _tasks = tasks.ToList();
        _candidates = candidates.ToList();
        _now = now;
        _dailyBoardTime = dailyBoardTime;
        _options = options;
        _filter = options.Filter;
        _openTaskAsync = openTaskAsync;
        _completeTaskAsync = completeTaskAsync;
        _dismissTaskAsync = dismissTaskAsync;
        _snoozeTaskAsync = snoozeTaskAsync;
        _setTaskDueAsync = setTaskDueAsync;
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
        _options = options;
        _filter = options.Filter;
        _now = now;
        _dailyBoardTime = dailyBoardTime;
        _tasks.Clear();
        _tasks.AddRange(tasks);
        _candidates.Clear();
        _candidates.AddRange(candidates);
        Render();
    }

    private void Render()
    {
        _now = DateTimeOffset.Now;
        var (actions, waiting, hiddenCandidateCount) = BuildBoardSections();
        var brief = DailyBriefPlanner.Build(_tasks, _candidates, _now);

        TitleText.Text = "업무 보드";
        SubtitleText.Text = _options.ShowBriefSummary
            ? $"{_now:yyyy-MM-dd HH:mm} 기준 · 오늘 브리핑에서 이어서 관리합니다. 기본 보드 시간 {_dailyBoardTime}"
            : $"{_now:yyyy-MM-dd HH:mm} 기준 · 활성 항목 전체 원장입니다. 기본 보드 시간 {_dailyBoardTime}";
        SummaryText.Text = $"{FilterLabel(_filter)} · 할 일 {actions.Length} · 대기 {waiting.Length}";
        BriefSummaryPanel.Visibility = _options.ShowBriefSummary ? Visibility.Visible : Visibility.Collapsed;
        BriefSummaryBodyText.Text = brief.TotalHighlights == 0
            ? "오늘 바로 볼 항목은 없습니다. 전체 보드에서 흐름을 확인하세요."
            : $"할 일 {brief.ActionItems.Count}개 · 대기 {brief.WaitingItems.Count}개";
        BriefSummaryMetaText.Text = brief.HiddenCandidateCount > 0
            ? $"검토 후보 {brief.HiddenCandidateCount}개는 필요할 때 검토 후보 탭에서 확인합니다."
            : "업무 보드의 오늘 필터와 같은 원장을 사용합니다.";
        OriginText.Text = OriginLabel(_options.Origin);
        FooterText.Text = _options.ShowBriefSummary
            ? "오늘 브리핑은 업무 보드에서 다시 볼 수 있습니다. 숨김/완료는 MailWhere 로컬 상태만 바꿉니다."
            : "숨김/완료는 MailWhere 업무 보드에서만 처리합니다. Outlook 원본은 그대로 유지됩니다.";
        ReviewCandidatesButton.Visibility = hiddenCandidateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewCandidatesButton.Content = $"검토 후보 {hiddenCandidateCount}개 보기";
        FillList(ActionList, actions, "표시할 할 일이 없습니다.");
        FillList(WaitingList, waiting, "표시할 대기 항목이 없습니다.");
        HighlightFilter();
    }

    private (LocalTaskItem[] Actions, LocalTaskItem[] Waiting, int HiddenCandidateCount) BuildBoardSections()
    {
        var visible = DailyBoardRouteTaskSelector.SelectVisibleTasks(
            _tasks,
            _candidates,
            _now,
            _filter,
            _options.ShowBriefSummary).ToArray();
        var actions = visible
            .Where(task => FollowUpPresentation.CategoryFor(task) == FollowUpDisplayCategory.ActionForMe)
            .OrderBy(SortKey)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        var waiting = visible
            .Where(task => FollowUpPresentation.CategoryFor(task) == FollowUpDisplayCategory.WaitingOnThem)
            .OrderBy(SortKey)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        var candidateCount = _candidates.Count(candidate => !candidate.Suppressed && (candidate.SnoozeUntil is null || candidate.SnoozeUntil <= _now));
        return (actions, waiting, candidateCount);
    }

    private void FillList(System.Windows.Controls.ListBox list, IReadOnlyList<LocalTaskItem> tasks, string emptyText)
    {
        list.ItemsSource = tasks.Count == 0
            ? new[] { BoardCardItem.Empty(emptyText) }
            : tasks.Select(task => BoardCardItem.FromTask(task, _now)).ToArray();
    }

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

    private static string OriginLabel(BoardOrigin origin) => origin switch
    {
        BoardOrigin.TrayToday => "트레이에서 열었습니다",
        BoardOrigin.DailyBriefToast => "오늘 브리핑 알림에서 열었습니다",
        BoardOrigin.ScheduledBriefFallback => "오늘 브리핑 대체 경로로 열었습니다",
        _ => "수동으로 열었습니다"
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
            FooterText.Text = $"원본 메일을 열지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void CompleteTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetCard(sender) is not { Task: { } task })
            {
                return;
            }

            if (!await _completeTaskAsync(task))
            {
                FooterText.Text = "이미 처리된 항목입니다.";
                return;
            }

            _tasks.RemoveAll(item => item.Id == task.Id);
            FooterText.Text = "완료 처리했습니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"완료 처리에 실패했습니다: {ex.GetType().Name}";
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
            FooterText.Text = "업무보드에서 숨겼습니다. Outlook 원본 메일은 그대로 유지됩니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"숨김 처리에 실패했습니다: {ex.GetType().Name}";
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
        menu.Items.Add(new WpfSeparator());
        AddMenuItem(menu, "보드에서 숨기기 (Outlook 유지)", () => DismissTaskFromMenuAsync(task));
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
                FooterText.Text = $"작업을 처리하지 못했습니다: {ex.GetType().Name}";
            }
        };
        menu.Items.Add(item);
    }

    private async Task DismissTaskFromMenuAsync(LocalTaskItem task)
    {
        try
        {
            if (!await _dismissTaskAsync(task))
            {
                FooterText.Text = "이미 처리된 항목입니다.";
                return;
            }

            _tasks.RemoveAll(item => item.Id == task.Id);
            FooterText.Text = "업무 보드에서 숨겼습니다. Outlook 원본 메일은 그대로 유지됩니다.";
            Render();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"숨김 처리에 실패했습니다: {ex.GetType().Name}";
        }
    }

    private async Task SnoozeTaskAsync(LocalTaskItem task, DateTimeOffset until, string message)
    {
        var now = DateTimeOffset.Now;
        var effectiveUntil = until <= now ? now.AddHours(1) : until;
        if (!await _snoozeTaskAsync(task, effectiveUntil))
        {
            FooterText.Text = "이미 처리된 항목이라 나중에 보기로 바꾸지 못했습니다.";
            return;
        }

        var index = _tasks.FindIndex(item => item.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task with { Status = LocalTaskStatus.Snoozed, SnoozeUntil = effectiveUntil, UpdatedAt = DateTimeOffset.UtcNow };
        }

        FooterText.Text = effectiveUntil == until ? message : $"{effectiveUntil:MM/dd HH:mm}에 다시 표시합니다.";
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
            FooterText.Text = $"기한 설정에 실패했습니다: {ex.GetType().Name}";
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
            FooterText.Text = "이미 처리된 항목이라 기한을 바꾸지 못했습니다.";
            return;
        }

        var index = _tasks.FindIndex(item => item.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task with { DueAt = dueAt, Status = task.Status == LocalTaskStatus.Snoozed ? LocalTaskStatus.Open : task.Status, SnoozeUntil = null, UpdatedAt = DateTimeOffset.UtcNow };
        }

        FooterText.Text = $"기한을 {dueAt:MM/dd}로 설정했습니다.";
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

    private async void ReviewCandidates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _openReviewCandidatesAsync();
        }
        catch (Exception ex)
        {
            FooterText.Text = $"검토 후보를 열지 못했습니다: {ex.GetType().Name}";
        }
    }

    private static BoardCardItem? GetCard(object sender) =>
        sender is FrameworkElement { Tag: BoardCardItem item } ? item : null;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class BoardCardItem
    {
        private BoardCardItem(LocalTaskItem? task, string title, string meta, string badge)
        {
            Task = task;
            Title = title;
            Meta = meta;
            Badge = badge;
        }

        public LocalTaskItem? Task { get; }
        public string Title { get; }
        public string Meta { get; }
        public string Badge { get; }
        public bool HasTask => Task is not null;
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task?.SourceId);
        public string DueButtonText => "기한 설정";
        public Visibility DueButtonVisibility => Task is null ? Visibility.Collapsed : Visibility.Visible;

        public static BoardCardItem Empty(string message) => new(null, message, string.Empty, "-");

        public static BoardCardItem FromTask(LocalTaskItem task, DateTimeOffset now)
        {
            var due = task.DueAt is null ? "기한 미정" : DdayFormatter.Format(task.DueAt.Value, now);
            var snooze = task.Status == LocalTaskStatus.Snoozed && task.SnoozeUntil is not null
                ? $" · 나중에 {task.SnoozeUntil:MM/dd HH:mm}"
                : string.Empty;
            var sender = string.IsNullOrWhiteSpace(task.SourceSenderDisplay) ? "직접 추가" : CompactLine(task.SourceSenderDisplay, 18);
            var received = task.SourceReceivedAt ?? task.CreatedAt;
            return new BoardCardItem(
                task,
                CompactLine(task.Title, 44),
                $"{due}{snooze} | {sender} | {received:MM/dd HH:mm}",
                FollowUpPresentation.CompactBadge(task.Kind));
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
