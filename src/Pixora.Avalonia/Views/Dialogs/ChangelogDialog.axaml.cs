using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pixora.Avalonia.Views.Dialogs;

public partial class ChangelogDialog : Window
{
    public ChangelogDialog() { InitializeComponent(); }

    public ChangelogDialog(string version, string releaseNotes, string releasePageUrl)
    {
        InitializeComponent();

        VersionLabel.Text = $"Pixora v{version}";
        NotesText.Text = string.IsNullOrWhiteSpace(releaseNotes)
            ? "No release notes available for this version."
            : releaseNotes;

        ReleasePageBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(releasePageUrl))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(releasePageUrl) { UseShellExecute = true });
        };

        CloseBtn.Click += (_, _) => Close();
    }
}
