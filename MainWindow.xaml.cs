using System.Windows;
using System.Windows.Controls;
using WinUtilDashboard.ViewModels;

namespace WinUtilDashboard;

/// <summary>
/// Code-behind is intentionally minimal. All logic lives in <see cref="MainViewModel"/>;
/// the view just binds to it. The view-model is resolved via DI in <see cref="App"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Start();
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// Auto-scroll the log TextBox whenever new text is appended.
    /// This is a legitimate view concern (visual behavior tied to a specific control),
    /// so it belongs in code-behind rather than the view-model.
    /// </summary>
    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ScrollToEnd();
    }
}
