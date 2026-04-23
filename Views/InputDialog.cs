using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinUtilDashboard.Views;

/// <summary>
/// Lightweight WPF input dialog - replaces Microsoft.VisualBasic.Interaction.InputBox
/// so we don't drag in the entire VisualBasic assembly just for a prompt.
/// </summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _input;

    public string ResponseText => _input.Text;

    public InputDialog(string prompt, string title, string defaultValue = "")
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(label, 0);

        _input = new TextBox
        {
            Text = defaultValue,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; Close(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
        Grid.SetRow(_input, 1);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => { DialogResult = true; Close(); };

        var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 2);

        grid.Children.Add(label);
        grid.Children.Add(_input);
        grid.Children.Add(buttonPanel);
        Content = grid;

        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    /// <summary>Shows the dialog. Returns null if cancelled.</summary>
    public static string? Prompt(Window owner, string prompt, string title, string defaultValue = "")
    {
        var dlg = new InputDialog(prompt, title, defaultValue) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.ResponseText : null;
    }
}
