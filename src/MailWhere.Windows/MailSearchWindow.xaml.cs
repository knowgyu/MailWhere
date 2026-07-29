using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MailWhere.Core.Domain;
using MailWhere.Core.Search;
using MailWhere.OutlookCom;
using MailWhere.Storage;

namespace MailWhere.Windows;

public partial class MailSearchWindow : Window
{
    private const int SearchLimit = 50;

    private readonly Func<string> _databasePathProvider;
    private CancellationTokenSource? _searchCancellation;
    private bool _openingSource;

    public MailSearchWindow(Func<string> databasePathProvider)
    {
        InitializeComponent();
        _databasePathProvider = databasePathProvider;
        ResultsList.Visibility = Visibility.Collapsed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;

        var query = QueryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ResultsList.ItemsSource = null;
            ResultsList.Visibility = Visibility.Collapsed;
            EmptyText.Text = "검색어를 입력하면 로컬 메일 인덱스에서 찾습니다.";
            EmptyText.Visibility = Visibility.Visible;
            SetSearchBusy(false, EmptyText.Text);
            return;
        }

        var databasePath = _databasePathProvider();
        if (!File.Exists(databasePath))
        {
            ResultsList.ItemsSource = null;
            ResultsList.Visibility = Visibility.Collapsed;
            EmptyText.Text = "메일 검색 인덱스가 아직 없습니다. 먼저 지금 메일 확인을 실행하세요.";
            EmptyText.Visibility = Visibility.Visible;
            SetSearchBusy(false, EmptyText.Text);
            return;
        }

        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        SetSearchBusy(true, "검색 중입니다…");
        ResultsList.ItemsSource = null;
        ResultsList.Visibility = Visibility.Collapsed;
        EmptyText.Text = "검색 중입니다…";
        EmptyText.Visibility = Visibility.Visible;

        try
        {
            await using var store = new SqliteMailMirrorStore(databasePath);
            await store.InitializeAsync(cancellationToken);
            var results = await store.SearchAsync(new MailMirrorSearchRequest(
                Query: query,
                Folder: SelectedFolder(),
                Limit: SearchLimit), cancellationToken);
            var rows = results.Select(MailSearchRow.FromResult).ToArray();

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ResultsList.ItemsSource = rows;
            ResultsList.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Text = rows.Length == 0
                ? "검색 결과가 없습니다. 다른 검색어를 입력해 보세요."
                : string.Empty;
            EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = rows.Length == 0 ? EmptyText.Text : $"{rows.Length}개를 찾았습니다.";

            if (rows.Length > 0)
            {
                ResultsList.SelectedIndex = 0;
                ResultsList.Focus();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ResultsList.Visibility = Visibility.Collapsed;
            EmptyText.Text = $"검색하지 못했습니다: {ex.GetType().Name}";
            EmptyText.Visibility = Visibility.Visible;
            StatusText.Text = EmptyText.Text;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetSearchBusy(false, StatusText.Text);
            }
        }
    }

    private MailSourceFolder? SelectedFolder() => FolderFilter.SelectedIndex switch
    {
        1 => MailSourceFolder.Inbox,
        2 => MailSourceFolder.Sent,
        _ => null
    };

    private async void OpenMail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MailSearchRow row })
        {
            await OpenMailAsync(row);
        }
    }

    private async void ResultsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is MailSearchRow row)
        {
            await OpenMailAsync(row);
        }
    }

    private async void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is MailSearchRow row)
        {
            e.Handled = true;
            await OpenMailAsync(row);
        }
    }

    private async Task OpenMailAsync(MailSearchRow row)
    {
        if (_openingSource || !row.CanOpen)
        {
            return;
        }

        _openingSource = true;
        ResultsList.IsEnabled = false;
        StatusText.Text = "Outlook에서 원본 메일을 여는 중입니다…";

        try
        {
            var locator = row.Locator;
            var result = await new OutlookComMailOpener().OpenAsync(locator.StoreId, locator.EntryId);
            StatusText.Text = result.Success
                ? "원본 메일을 열었습니다."
                : $"원본 메일을 열지 못했습니다: {result.StatusCode}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"원본 메일을 열지 못했습니다: {ex.GetType().Name}";
        }
        finally
        {
            _openingSource = false;
            ResultsList.IsEnabled = true;
        }
    }

    private void SetSearchBusy(bool busy, string status)
    {
        SearchButton.IsEnabled = !busy;
        StatusText.Text = status;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        base.OnClosed(e);
    }

    private sealed record MailSearchRow(
        MailMirrorLocator Locator,
        string Subject,
        string Meta,
        string Snippet)
    {
        public bool CanOpen => Locator.IsValid;

        public static MailSearchRow FromResult(MailMirrorSearchResult result)
        {
            var date = (result.ReceivedAt ?? result.SentAt)?.ToLocalTime().ToString("M/d HH:mm") ?? "날짜 없음";
            var folder = result.Folder switch
            {
                MailSourceFolder.Inbox => "받은 메일",
                MailSourceFolder.Sent => "보낸 메일",
                _ => "기타"
            };

            return new MailSearchRow(
                result.Locator,
                Compact(result.Subject, 140),
                $"{Compact(result.SenderDisplay, 80)} · {date} · {folder}",
                Compact(result.Snippet, 220));
        }

        private static string Compact(string? value, int maxChars)
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
