using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Pixora.Avalonia.ViewModels;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Views;

public partial class DownloadByImageIdView : UserControl
{
    public DownloadByImageIdView()
    {
        InitializeComponent();
    }

    private async void OnPasteClicked(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        var text = await clipboard.TryGetTextAsync();
        if (DataContext is DownloadByImageIdViewModel vm)
            vm.PasteFromClipboard(text);
    }
}
