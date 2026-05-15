using System.Windows;

namespace MailWhere.Windows;

public partial class DueDateDialog : Window
{
    public DueDateDialog(DateTime today, DateTime? selectedDate = null)
    {
        InitializeComponent();
        DueDatePicker.DisplayDate = today.Date;
        DueDatePicker.SelectedDate = selectedDate?.Date ?? today.Date;
    }

    public DateTimeOffset? SelectedDueAt { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = DueDatePicker.SelectedDate ?? DateTime.Today;
        SelectedDueAt = new DateTimeOffset(selected.Year, selected.Month, selected.Day, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(selected));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
