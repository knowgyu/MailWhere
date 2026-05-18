using System.Windows;
using MailWhere.Core.Domain;

namespace MailWhere.Windows;

public partial class TaskEditDialog : Window
{
    private readonly FollowUpKind _kind;

    public TaskEditDialog(LocalTaskItem task)
    {
        InitializeComponent();
        _kind = TaskEditRequest.NormalizeKind(task.Kind);
        TitleText.Text = FollowUpPresentation.ActionTitle(task.Title);
        DueDatePicker.DisplayDate = DateTime.Today;
        DueDatePicker.SelectedDate = task.DueAt?.DateTime.Date ?? DateTime.Today;
        NoDueCheck.IsChecked = task.DueAt is null;
        UpdateDueAvailability();
        Loaded += (_, _) =>
        {
            TitleText.Focus();
            TitleText.SelectAll();
        };
    }

    public TaskEditRequest? EditRequest { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DateTimeOffset? dueAt = NoDueCheck.IsChecked == true
                ? null
                : BuildDueDate(DueDatePicker.SelectedDate ?? DateTime.Today);
            EditRequest = TaskEditRequest.Create(TitleText.Text, _kind, dueAt);
            DialogResult = true;
        }
        catch (ArgumentException)
        {
            ErrorText.Text = "제목을 입력해주세요.";
            ErrorText.Visibility = Visibility.Visible;
            TitleText.Focus();
        }
    }

    private static DateTimeOffset BuildDueDate(DateTime selected) =>
        new(selected.Year, selected.Month, selected.Day, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(selected));

    private void NoDue_Click(object sender, RoutedEventArgs e) => UpdateDueAvailability();

    private void UpdateDueAvailability() =>
        DueDatePicker.IsEnabled = NoDueCheck.IsChecked != true;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            Save_Click(sender, e);
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
