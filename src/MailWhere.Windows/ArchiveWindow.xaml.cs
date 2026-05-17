using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;

namespace MailWhere.Windows;

public partial class ArchiveWindow : Window
{
    private IReadOnlyList<LocalTaskItem> _tasks;
    private readonly Func<LocalTaskItem, Task> _openMailAsync;
    private readonly Func<LocalTaskItem, Task<bool>> _restoreAsync;

    public ArchiveWindow(
        IReadOnlyList<LocalTaskItem> tasks,
        Func<LocalTaskItem, Task> openMailAsync,
        Func<LocalTaskItem, Task<bool>> restoreAsync)
    {
        InitializeComponent();
        _tasks = tasks;
        _openMailAsync = openMailAsync;
        _restoreAsync = restoreAsync;
        Render();
    }

    public void Refresh(IReadOnlyList<LocalTaskItem> tasks)
    {
        _tasks = tasks;
        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var rows = _tasks.Select(task => ArchiveRow.FromTask(task, now)).ToArray();
        ArchiveList.ItemsSource = rows;
        ArchiveList.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length > 0 && ArchiveList.SelectedIndex < 0)
        {
            ArchiveList.SelectedIndex = 0;
        }

        StatusText.Text = rows.Length == 0 ? "보관한 업무가 없습니다." : $"보관한 업무 {rows.Length}개";
    }

    private async void OpenMail_Click(object sender, RoutedEventArgs e) => await RunAsync(sender, _openMailAsync, "원본 메일을 열었습니다.");

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ArchiveRow row })
        {
            await RestoreAsync(row.Task);
        }
    }

    private async void ArchiveList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveRow row && row.CanOpen)
        {
            await ExecuteAsync(row.Task, _openMailAsync, "원본 메일을 열었습니다.");
        }
    }

    private async Task RunAsync(object sender, Func<LocalTaskItem, Task> action, string successMessage)
    {
        if (sender is FrameworkElement { Tag: ArchiveRow row })
        {
            await ExecuteAsync(row.Task, action, successMessage);
        }
    }

    private async Task ExecuteAsync(LocalTaskItem task, Func<LocalTaskItem, Task> action, string successMessage)
    {
        try
        {
            await action(task);
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"처리하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async Task RestoreAsync(LocalTaskItem task)
    {
        try
        {
            var restored = await _restoreAsync(task);
            if (restored)
            {
                _tasks = _tasks.Where(item => item.Id != task.Id).ToArray();
                Render();
                StatusText.Text = "업무 보드로 복원했습니다.";
            }
            else
            {
                StatusText.Text = "이미 처리된 항목입니다.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"복원하지 못했습니다: {ex.GetType().Name}";
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

    private sealed record ArchiveRow(LocalTaskItem Task, string Title, string Meta)
    {
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task.SourceId);

        public static ArchiveRow FromTask(LocalTaskItem task, DateTimeOffset now)
        {
            var due = FollowUpPresentation.HumanDueText(task.DueAt, now);
            var sender = FollowUpPresentation.HumanSenderText(task.SourceSenderDisplay);
            var archivedAt = task.UpdatedAt.ToOffset(now.Offset).ToString("M/d HH:mm");
            return new ArchiveRow(
                task,
                FollowUpPresentation.ActionTitle(task.Title),
                $"{due} · {sender} · 보관 {archivedAt}");
        }
    }
}
