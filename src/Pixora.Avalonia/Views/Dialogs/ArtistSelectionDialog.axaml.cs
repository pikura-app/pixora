using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pixora.Avalonia.Views.Dialogs;

public partial class ArtistSelectionDialog : Window
{
    public ArtistSelectionDialog()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
