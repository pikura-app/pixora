using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pikura.Avalonia.Views.Login;

public partial class ManualCookieDialog : Window
{
    public string? PhpSessId { get; private set; }

    public ManualCookieDialog() { InitializeComponent(); }

    /// <summary>
    /// Optional one-liner explaining WHY the user is seeing this fallback dialog
    /// (e.g. "Embedded browser couldn't load."). Shown above the instructions
    /// so users know this isn't the normal sign-in flow.
    /// </summary>
    public void SetReason(string reason)
    {
        if (ReasonText == null) return;
        ReasonText.Text = reason;
        ReasonText.IsVisible = true;
    }

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
