using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MailWhere.Windows;

public partial class ToastNotificationWindow : Window
{
    private readonly Func<Task> _primaryAction;
    private readonly Func<Task>? _secondaryAction;
    private readonly DispatcherTimer _dismissTimer;
    private bool _actionRunning;

    internal ToastNotificationWindow(ToastNotificationSpec spec, Func<Task> primaryAction, Func<Task>? secondaryAction)
    {
        InitializeComponent();

        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;
        _dismissTimer = new DispatcherTimer
        {
            Interval = spec.Duration
        };
        _dismissTimer.Tick += (_, _) => Close();

        ApplySpec(spec);

        Loaded += (_, _) => _dismissTimer.Start();
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => _dismissTimer.Start();
    }

    public event EventHandler? ToastClosed;

    public double StackHeight => ActualHeight > 0 ? ActualHeight : 156;

    public void MoveTo(double left, double top)
    {
        Left = left;
        Top = top;
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        ToastClosed?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }

    private void ApplySpec(ToastNotificationSpec spec)
    {
        KindText.Text = spec.KindLabel;
        TitleText.Text = spec.Title;
        MessageText.Text = spec.Message;
        MetaText.Text = spec.MetaText;
        IconGlyph.Text = spec.IconGlyph;
        PrimaryButton.Content = spec.PrimaryLabel;
        SecondaryButton.Content = spec.SecondaryLabel ?? string.Empty;
        SecondaryButton.Visibility = string.IsNullOrWhiteSpace(spec.SecondaryLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;

        AccentBar.Background = ToBrush(spec.AccentColor);
        IconGlyph.Foreground = ToBrush(spec.AccentColor);
        IconBadge.Background = ToBrush(spec.BadgeColor);
    }

    private async void ToastCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        await RunActionAsync(_primaryAction);
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(_primaryAction);

    private async void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_secondaryAction is null)
        {
            Close();
            return;
        }

        await RunActionAsync(_secondaryAction);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_actionRunning)
        {
            return;
        }

        _actionRunning = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch
        {
            // A toast action must never crash the notification surface.
        }
        finally
        {
            Close();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static System.Windows.Media.Brush ToBrush(string hex)
    {
        var brush = (System.Windows.Media.Brush?)new BrushConverter().ConvertFromString(hex);
        return brush ?? System.Windows.Media.Brushes.SteelBlue;
    }
}

internal sealed record ToastNotificationSpec(
    string KindLabel,
    string Title,
    string Message,
    string MetaText,
    string IconGlyph,
    string AccentColor,
    string BadgeColor,
    string PrimaryLabel,
    string? SecondaryLabel,
    TimeSpan Duration);
