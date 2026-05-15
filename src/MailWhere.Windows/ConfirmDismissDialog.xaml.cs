using System.Windows;

namespace MailWhere.Windows;

public partial class ConfirmDismissDialog : Window
{
    public ConfirmDismissDialog()
    {
        InitializeComponent();
    }

    public bool DoNotAskAgain => DoNotAskAgainBox.IsChecked == true;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
