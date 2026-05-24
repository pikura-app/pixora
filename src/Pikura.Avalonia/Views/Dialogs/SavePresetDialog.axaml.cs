using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pikura.Avalonia.Views.Dialogs;

public partial class SavePresetDialog : Window
{
    public string PresetName => PresetNameTextBox.Text?.Trim() ?? string.Empty;
    public string Description => DescriptionTextBox.Text?.Trim() ?? string.Empty;

    public SavePresetDialog()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PresetName))
        {
            // Could show error message
            return;
        }
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
