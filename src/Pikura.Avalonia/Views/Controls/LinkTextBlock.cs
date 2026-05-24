using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Pikura.Avalonia.Views.Controls;

/// <summary>
/// A TextBlock that auto-detects URLs and pixiv.net links in its text
/// and renders them as clickable underlined inline buttons.
/// </summary>
public sealed class LinkTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> LinkTextProperty =
        AvaloniaProperty.Register<LinkTextBlock, string?>(nameof(LinkText));

    public string? LinkText
    {
        get => GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    private static readonly Regex UrlRegex = new(
        @"(https?://[^\s""'<>]+|pixiv\.net/[^\s""'<>]+|[A-Za-z]:\\[^\n""'<>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static LinkTextBlock()
    {
        LinkTextProperty.Changed.AddClassHandler<LinkTextBlock>((b, _) => b.Rebuild());
    }

    private void Rebuild()
    {
        var raw = LinkText;
        Inlines?.Clear();
        Text = null;

        if (string.IsNullOrEmpty(raw))
            return;

        var inlines = new InlineCollection();
        int pos = 0;

        foreach (Match m in UrlRegex.Matches(raw))
        {
            if (m.Index > pos)
                inlines.Add(new Run(raw[pos..m.Index]));

            var url = m.Value;
            var href = (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        || (url.Length >= 3 && url[1] == ':' && url[2] == '\\'))
                ? url
                : "https://" + url;

            var underline = new TextDecorationCollection
            {
                new TextDecoration { Location = TextDecorationLocation.Underline }
            };
            var btn = new TextBlock
            {
                Text = url,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextDecorations = underline,
                Foreground = new SolidColorBrush(Color.Parse("#56A0DB")),
            };

            var captured = href;
            btn.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
                {
                    try
                    {
                        // Windows file/folder path — open in Explorer
                        if (captured.Length >= 3 && captured[1] == ':' && captured[2] == '\\')
                        {
                            if (System.IO.File.Exists(captured))
                                Process.Start("explorer.exe", $"/select,\"{captured}\"");
                            else
                                Process.Start(new ProcessStartInfo(captured) { UseShellExecute = true });
                        }
                        else
                        {
                            Process.Start(new ProcessStartInfo(captured) { UseShellExecute = true });
                        }
                    }
                    catch { }
                    e.Handled = true;
                }
            };

            inlines.Add(new InlineUIContainer { Child = btn, BaselineAlignment = BaselineAlignment.TextBottom });
            pos = m.Index + m.Length;
        }

        if (pos < raw.Length)
            inlines.Add(new Run(raw[pos..]));

        Inlines = inlines;
    }
}
