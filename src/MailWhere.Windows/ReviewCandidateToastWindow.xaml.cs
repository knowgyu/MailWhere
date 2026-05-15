using System.Windows;
using System.Windows.Input;
using MailWhere.Core.Domain;

namespace MailWhere.Windows;

public partial class ReviewCandidateToastWindow : Window
{
    private readonly Func<Task> _approveAsync;
    private readonly Func<Task> _ignoreAsync;
    private bool _handled;
    private bool _shortcutsEnabled;

    public ReviewCandidateToastWindow(ReviewCandidate candidate, Func<Task> approveAsync, Func<Task> ignoreAsync)
    {
        InitializeComponent();
        _approveAsync = approveAsync;
        _ignoreAsync = ignoreAsync;

        TitleText.Text = string.IsNullOrWhiteSpace(candidate.Analysis.SuggestedTitle)
            ? "메일 확인 후보"
            : candidate.Analysis.SuggestedTitle;
        ReasonText.Text = candidate.Analysis.Reason;
        EvidenceText.Text = candidate.Analysis.EvidenceSnippet ?? "짧은 근거가 없습니다.";

        Loaded += (_, _) => PlaceAtBottomRight();
        MouseDown += (_, _) => ActivateForShortcuts();
        KeyDown += ReviewCandidateToastWindow_KeyDown;
    }

    private async void ReviewCandidateToastWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_shortcutsEnabled)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Y)
        {
            e.Handled = true;
            await CompleteAsync(_approveAsync);
        }
        else if (e.Key == Key.N)
        {
            e.Handled = true;
            await CompleteAsync(_ignoreAsync);
        }
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await CompleteAsync(_approveAsync);

    private async void Ignore_Click(object sender, RoutedEventArgs e) => await CompleteAsync(_ignoreAsync);

    private async Task CompleteAsync(Func<Task> action)
    {
        if (_handled)
        {
            return;
        }

        _handled = true;
        try
        {
            await action();
        }
        finally
        {
            Close();
        }
    }

    private void PlaceAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void ActivateForShortcuts()
    {
        _shortcutsEnabled = true;
        Activate();
        Focus();
        Keyboard.Focus(this);
    }
}
