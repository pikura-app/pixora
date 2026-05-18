using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pixora.Avalonia.Views.Login;

public partial class ManualCookieDialog : Window
{
    public string? PhpSessId { get; private set; }

    public ManualCookieDialog() { InitializeComponent(); }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        var value = CookieInput.Text?.Trim();
        if (!string.IsNullOrEmpty(value))
        {
            PhpSessId = value;
            Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
