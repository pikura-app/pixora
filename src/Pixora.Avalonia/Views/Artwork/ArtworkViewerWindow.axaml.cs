using Avalonia.Controls;
using Pixora.Avalonia.ViewModels;
using Pixora.Core.Models;
using Pixora.Avalonia.Services;

namespace Pixora.Avalonia.Views.Artwork;

public partial class ArtworkViewerWindow : Window
{
    public ArtworkViewerWindow() { InitializeComponent(); }

    public ArtworkViewerWindow(ArtworkPreview artwork, GalleryViewModel gallery)
    {
        InitializeComponent();
        var vm = new ArtworkViewerViewModel(artwork, gallery, this);
        DataContext = vm;
        _ = vm.LoadFirstPageAsync();
    }
}
