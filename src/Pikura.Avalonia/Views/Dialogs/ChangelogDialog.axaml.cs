using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pikura.Avalonia.Views.Dialogs;

public partial class ChangelogDialog : Window
{
    public ChangelogDialog() { InitializeComponent(); }

    public ChangelogDialog(string version, string releaseNotes, string releasePageUrl)
    {
        InitializeComponent();

        VersionLabel.Text = $"Pikura v{version}";

        var section = ExtractVersionSection(releaseNotes, version);
        RenderMarkdown(string.IsNullOrWhiteSpace(section)
            ? "No release notes available for this version."
            : section);

        ReleasePageBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(releasePageUrl))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(releasePageUrl) { UseShellExecute = true });
        };

        CloseBtn.Click += (_, _) => Close();
    }

    /// <summary>
    /// Extracts only the section for the given version from the full release notes,
    /// stopping before the next "## " heading (the previous release).
    /// </summary>
    private static string ExtractVersionSection(string fullNotes, string version)
    {
        if (string.IsNullOrWhiteSpace(fullNotes)) return string.Empty;
        var lines = fullNotes.Replace("\r\n", "\n").Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart('#').Trim().Contains(version, StringComparison.OrdinalIgnoreCase)
                && lines[i].StartsWith("##"))
            { start = i + 1; break; }
        }
        if (start < 0) return fullNotes; // fallback: show everything
        var sb = new System.Text.StringBuilder();
        for (var i = start; i < lines.Length; i++)
        {
            // Stop at the next top-level version heading (but not sub-headings like ###)
            if (i > start && lines[i].StartsWith("## ")) break;
            // Skip horizontal rules
            if (lines[i].TrimStart('-').Trim() == string.Empty && lines[i].Contains('-') && lines[i].Length > 2) continue;
            sb.AppendLine(lines[i]);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Renders a subset of markdown into the NotesPanel as formatted controls.</summary>
    private void RenderMarkdown(string markdown)
    {
        NotesPanel.Children.Clear();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (string.IsNullOrEmpty(line))
            {
                NotesPanel.Children.Add(new TextBlock { Height = 6 });
                continue;
            }

            // ### sub-heading
            if (line.StartsWith("### "))
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = line[4..].Trim(),
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 10, 0, 2),
                    TextWrapping = TextWrapping.Wrap,
                });
                continue;
            }

            // ## heading (shouldn't appear after extraction, but just in case)
            if (line.StartsWith("## "))
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = line[3..].Trim(),
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 8, 0, 4),
                    TextWrapping = TextWrapping.Wrap,
                });
                continue;
            }

            // Bullet line: starts with "- "
            if (line.StartsWith("- "))
            {
                var content = line[2..].Trim();
                var tb = BuildInlineTextBlock(content, indent: true);
                NotesPanel.Children.Add(tb);
                continue;
            }

            // Plain paragraph
            NotesPanel.Children.Add(BuildInlineTextBlock(line, indent: false));
        }
    }

    /// <summary>Builds a TextBlock with inline bold (**text**) support.</summary>
    private static TextBlock BuildInlineTextBlock(string text, bool indent)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(indent ? 10 : 0, 1, 0, 1),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        };

        // Split on **bold** markers
        var parts = Regex.Split(text, @"\*\*(.+?)\*\*");
        var isBold = false;
        var prefixAdded = false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) { isBold = !isBold; continue; }

            if (indent && !prefixAdded)
            {
                tb.Inlines!.Add(new Run("• ") { FontWeight = FontWeight.Bold });
                prefixAdded = true;
            }

            tb.Inlines!.Add(new Run(part)
            {
                FontWeight = isBold ? FontWeight.SemiBold : FontWeight.Normal,
            });
            isBold = !isBold;
        }

        // If no inlines were added (e.g. no bold), set Text directly
        if (tb.Inlines?.Count == 0)
        {
            tb.Text = indent ? $"• {text}" : text;
        }

        return tb;
    }
}
