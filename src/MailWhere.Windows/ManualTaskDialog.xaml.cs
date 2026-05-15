using System.Windows;

namespace MailWhere.Windows;

public partial class ManualTaskDialog : Window
{
    public ManualTaskDialog(DateTime today)
    {
        InitializeComponent();
        DueDatePicker.DisplayDate = today.Date;
    }

    public string TaskTitle { get; private set; } = string.Empty;
    public DateTimeOffset? DueAt { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TaskTitle = TitleText.Text.Trim();
        if (string.IsNullOrWhiteSpace(TaskTitle))
        {
            TitleText.Focus();
            return;
        }

        if (DueDatePicker.SelectedDate is { } selected)
        {
            DueAt = new DateTimeOffset(selected.Year, selected.Month, selected.Day, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(selected));
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
