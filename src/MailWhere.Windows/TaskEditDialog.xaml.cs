using System.Windows;
using System.Linq;
using MailWhere.Core.Domain;

namespace MailWhere.Windows;

public partial class TaskEditDialog : Window
{
    private sealed record KindChoice(string Label, FollowUpKind Kind);

    public TaskEditDialog(LocalTaskItem task)
    {
        InitializeComponent();
        TitleText.Text = task.Title;
        KindBox.ItemsSource = new[]
        {
            new KindChoice("할 일", FollowUpKind.ActionRequested),
            new KindChoice("일정", FollowUpKind.Meeting),
            new KindChoice("기다리는 중", FollowUpKind.WaitingForReply)
        };
        var normalizedKind = TaskEditRequest.NormalizeKind(task.Kind);
        KindBox.SelectedItem = KindBox.Items.Cast<KindChoice>().FirstOrDefault(choice => choice.Kind == normalizedKind)
                               ?? KindBox.Items[0];
        DueDatePicker.DisplayDate = DateTime.Today;
        DueDatePicker.SelectedDate = task.DueAt?.DateTime.Date ?? DateTime.Today;
        NoDueCheck.IsChecked = task.DueAt is null;
        UpdateDueAvailability();
        TitleText.SelectAll();
        TitleText.Focus();
    }

    public TaskEditRequest? EditRequest { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var kind = KindBox.SelectedItem is KindChoice choice
                ? choice.Kind
                : FollowUpKind.ActionRequested;
            DateTimeOffset? dueAt = NoDueCheck.IsChecked == true
                ? null
                : BuildDueDate(DueDatePicker.SelectedDate ?? DateTime.Today);
            EditRequest = TaskEditRequest.Create(TitleText.Text, kind, dueAt);
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
