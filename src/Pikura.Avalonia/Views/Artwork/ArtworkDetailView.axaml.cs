using Avalonia.Controls;
using Avalonia.Input;
using Pikura.Avalonia.ViewModels;
using Pikura.Core.Models;

namespace Pikura.Avalonia.Views.Artwork;

public partial class ArtworkDetailView : UserControl
{
    public ArtworkDetailView()
    {
        InitializeComponent();
    }

    public void Initialize(ArtworkPreview artwork)
    {
        if (DataContext is ArtworkDetailViewModel viewModel)
        {
            viewModel.Initialize(artwork);
        }
    }

    private void OnTagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border border) return;
        if (border.DataContext is not string tag) return;
        if (DataContext is not ArtworkDetailViewModel vm) return;

        if (vm.SearchByTagCommand.CanExecute(tag))
        {
            _ = vm.SearchByTagCommand.ExecuteAsync(tag);
        }
        e.Handled = true;
    }
}