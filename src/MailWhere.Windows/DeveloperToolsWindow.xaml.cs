using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Scheduling;

namespace MailWhere.Windows;

public partial class DeveloperToolsWindow : Window
{
    private readonly Func<BoardRouteFilter, Task> _openFilterAsync;
    private readonly Func<Task> _showToastAsync;
    private readonly Func<Task> _resetTodayMarkerAsync;
    private readonly Func<Task> _addSampleTasksAsync;
    private readonly Func<Task> _addSampleReviewAsync;

    public DeveloperToolsWindow(
        Func<BoardRouteFilter, Task> openFilterAsync,
        Func<Task> showToastAsync,
        Func<Task> resetTodayMarkerAsync,
        Func<Task> addSampleTasksAsync,
        Func<Task> addSampleReviewAsync)
    {
        InitializeComponent();
        _openFilterAsync = openFilterAsync;
        _showToastAsync = showToastAsync;
        _resetTodayMarkerAsync = resetTodayMarkerAsync;
        _addSampleTasksAsync = addSampleTasksAsync;
        _addSampleReviewAsync = addSampleReviewAsync;
    }

    private async void OpenToday_Click(object sender, RoutedEventArgs e) => await RunAsync(() => _openFilterAsync(BoardRouteFilter.Today), "오늘 화면을 열었습니다.");
    private async void OpenWeek_Click(object sender, RoutedEventArgs e) => await RunAsync(() => _openFilterAsync(BoardRouteFilter.Week), "이번 주 화면을 열었습니다.");
    private async void OpenNoDue_Click(object sender, RoutedEventArgs e) => await RunAsync(() => _openFilterAsync(BoardRouteFilter.NoDue), "날짜 없음 화면을 열었습니다.");
    private async void OpenAll_Click(object sender, RoutedEventArgs e) => await RunAsync(() => _openFilterAsync(BoardRouteFilter.All), "전체 화면을 열었습니다.");
    private async void Toast_Click(object sender, RoutedEventArgs e) => await RunAsync(_showToastAsync, "알림 테스트를 보냈습니다.");
    private async void SampleTasks_Click(object sender, RoutedEventArgs e) => await RunAsync(_addSampleTasksAsync, "샘플 업무를 추가했습니다.");
    private async void SampleReview_Click(object sender, RoutedEventArgs e) => await RunAsync(_addSampleReviewAsync, "샘플 검토 후보를 추가했습니다.");
    private async void ResetTodayMarker_Click(object sender, RoutedEventArgs e) => await RunAsync(_resetTodayMarkerAsync, "오늘 표시 기록을 초기화했습니다.");

    private async Task RunAsync(Func<Task> action, string successMessage)
    {
        try
        {
            await action();
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"처리하지 못했습니다: {ex.GetType().Name}";
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
}
