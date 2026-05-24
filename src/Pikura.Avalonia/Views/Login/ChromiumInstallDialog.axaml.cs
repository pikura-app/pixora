using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading;

namespace Pikura.Avalonia.Views.Login;

/// <summary>
/// Modal dialog shown the first time the user signs in on Linux (and on
/// other platforms if they ever need to re-install Chromium). It surfaces
/// progress while <c>Microsoft.Playwright.Program.Main(["install", "chromium"])</c>
/// runs on a background thread, and offers a Cancel button that flips the
/// caller's <see cref="CancellationTokenSource"/>.
///
/// We can't get fine-grained progress out of the Playwright installer (it just
/// writes to stdout and returns when done), so the bar is indeterminate. The
/// detail line is a friendly status the caller can update if they want.
/// </summary>
public partial class ChromiumInstallDialog : Window
{
    private readonly CancellationTokenSource _cts;

    /// <summary>True when the user clicked Cancel — caller should not proceed.</summary>
    public bool Cancelled { get; private set; }

    public ChromiumInstallDialog() : this(new CancellationTokenSource()) { }

    public ChromiumInstallDialog(CancellationTokenSource cts)
    {
        _cts = cts;
        InitializeComponent();
    }

    /// <summary>Updates the secondary detail line from any thread.</summary>
    public void SetDetail(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (DetailText != null) DetailText.Text = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() => { if (DetailText != null) DetailText.Text = text; });
        }
    }

    /// <summary>Updates the primary status text from any thread.</summary>
    public void SetStatus(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (StatusText != null) StatusText.Text = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() => { if (StatusText != null) StatusText.Text = text; });
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Cancelled = true;
        try { _cts.Cancel(); } catch { /* already cancelled */ }
        Close();
    }
}
