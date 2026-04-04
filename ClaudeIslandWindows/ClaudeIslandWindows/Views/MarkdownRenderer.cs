using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ClaudeIslandWindows.Views;

/// <summary>
/// Terminal color palette matching macOS TerminalColors.
/// </summary>
public static class TerminalColors
{
    public static readonly SolidColorBrush Green = new(Color.FromRgb(102, 191, 115));
    public static readonly SolidColorBrush Amber = new(Color.FromRgb(255, 179, 0));
    public static readonly SolidColorBrush Red = new(Color.FromRgb(255, 77, 77));
    public static readonly SolidColorBrush Cyan = new(Color.FromRgb(0, 204, 204));
    public static readonly SolidColorBrush Blue = new(Color.FromRgb(102, 153, 255));
    public static readonly SolidColorBrush Magenta = new(Color.FromRgb(204, 102, 204));
    public static readonly SolidColorBrush Prompt = new(Color.FromRgb(217, 120, 87));
    public static readonly SolidColorBrush Dim = new(Color.FromArgb(102, 255, 255, 255));
    public static readonly SolidColorBrush Dimmer = new(Color.FromArgb(51, 255, 255, 255));
    public static readonly SolidColorBrush Text90 = new(Color.FromArgb(230, 255, 255, 255));
    public static readonly SolidColorBrush Text70 = new(Color.FromArgb(180, 255, 255, 255));
    public static readonly SolidColorBrush Text40 = new(Color.FromArgb(102, 255, 255, 255));
    public static readonly SolidColorBrush CodeBg = new(Color.FromArgb(20, 255, 255, 255));
}

/// <summary>
/// Renders markdown text into WPF TextBlock with inline formatting.
/// Supports: **bold**, *italic*, `inline code`, ```code blocks```,
/// headings (#), lists (- *), links [text](url), ~~strikethrough~~
/// </summary>
public static partial class MarkdownRenderer
{
    private static readonly FontFamily MonoFont = new("Consolas");
    private static readonly FontFamily UIFont = new("Segoe UI");

    public static UIElement Render(string text, double fontSize = 12)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TextBlock();

        var lines = text.Split('\n');
        var panel = new StackPanel();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Code block (``` or indented)
            if (line.TrimStart().StartsWith("```"))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                i++; // skip closing ```
                panel.Children.Add(CreateCodeBlock(string.Join("\n", codeLines)));
                continue;
            }

            // Heading
            if (line.StartsWith("### "))
            {
                panel.Children.Add(CreateHeading(line[4..], 3, fontSize));
                i++; continue;
            }
            if (line.StartsWith("## "))
            {
                panel.Children.Add(CreateHeading(line[3..], 2, fontSize));
                i++; continue;
            }
            if (line.StartsWith("# "))
            {
                panel.Children.Add(CreateHeading(line[2..], 1, fontSize));
                i++; continue;
            }

            // Unordered list
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var indent = line.Length - line.TrimStart().Length;
                var bulletText = line.TrimStart()[2..];
                panel.Children.Add(CreateListItem("•", bulletText, indent, fontSize));
                i++; continue;
            }

            // Ordered list
            var orderedMatch = OrderedListRegex().Match(line.TrimStart());
            if (orderedMatch.Success)
            {
                var num = orderedMatch.Groups[1].Value;
                var listText = line.TrimStart()[(orderedMatch.Length)..];
                panel.Children.Add(CreateListItem($"{num}.", listText, 0, fontSize));
                i++; continue;
            }

            // Horizontal rule
            if (line.Trim() is "---" or "***" or "___")
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = TerminalColors.Dimmer,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                i++; continue;
            }

            // Empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                panel.Children.Add(new Border { Height = 6 });
                i++; continue;
            }

            // Regular paragraph with inline formatting
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = UIFont,
                FontSize = fontSize,
                Margin = new Thickness(0, 1, 0, 1)
            };
            AddInlineFormatting(tb, line);
            panel.Children.Add(tb);
            i++;
        }

        return panel;
    }

    private static UIElement CreateCodeBlock(string code)
    {
        return new Border
        {
            Background = TerminalColors.CodeBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 4),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static UIElement CreateHeading(string text, int level, double fontSize)
    {
        var tb = new TextBlock
        {
            FontFamily = UIFont,
            FontWeight = FontWeights.Bold,
            FontSize = level == 1 ? fontSize + 4 : level == 2 ? fontSize + 2 : fontSize,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 2)
        };
        if (level == 1) tb.FontStyle = FontStyles.Italic;
        AddInlineFormatting(tb, text);
        return tb;
    }

    private static UIElement CreateListItem(string bullet, string text, int indent, double fontSize)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent * 8, 1, 0, 1) };
        panel.Children.Add(new TextBlock
        {
            Text = bullet,
            FontFamily = UIFont,
            FontSize = fontSize,
            Foreground = TerminalColors.Dim,
            Width = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 4, 0)
        });

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = UIFont,
            FontSize = fontSize
        };
        AddInlineFormatting(tb, text);
        panel.Children.Add(tb);
        return panel;
    }

    /// <summary>
    /// Parse inline markdown: **bold**, *italic*, `code`, ~~strike~~, [link](url)
    /// </summary>
    private static void AddInlineFormatting(TextBlock tb, string text)
    {
        var parts = InlineRegex().Matches(text);
        int lastEnd = 0;

        foreach (Match match in parts)
        {
            // Add plain text before this match
            if (match.Index > lastEnd)
                AddPlainRun(tb, text[lastEnd..match.Index]);

            if (match.Groups["bold"].Success)
            {
                tb.Inlines.Add(new Run(match.Groups["bold"].Value)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = TerminalColors.Text90
                });
            }
            else if (match.Groups["italic"].Success)
            {
                tb.Inlines.Add(new Run(match.Groups["italic"].Value)
                {
                    FontStyle = FontStyles.Italic,
                    Foreground = TerminalColors.Text90
                });
            }
            else if (match.Groups["code"].Success)
            {
                tb.Inlines.Add(new Run(match.Groups["code"].Value)
                {
                    FontFamily = MonoFont,
                    Background = TerminalColors.CodeBg,
                    Foreground = TerminalColors.Text90
                });
            }
            else if (match.Groups["strike"].Success)
            {
                tb.Inlines.Add(new Run(match.Groups["strike"].Value)
                {
                    TextDecorations = TextDecorations.Strikethrough,
                    Foreground = TerminalColors.Text40
                });
            }
            else if (match.Groups["linktext"].Success)
            {
                tb.Inlines.Add(new Run(match.Groups["linktext"].Value)
                {
                    Foreground = TerminalColors.Blue,
                    TextDecorations = TextDecorations.Underline
                });
            }

            lastEnd = match.Index + match.Length;
        }

        // Remaining plain text
        if (lastEnd < text.Length)
            AddPlainRun(tb, text[lastEnd..]);
    }

    private static void AddPlainRun(TextBlock tb, string text)
    {
        if (!string.IsNullOrEmpty(text))
            tb.Inlines.Add(new Run(text) { Foreground = TerminalColors.Text90 });
    }

    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|\*(?<italic>[^*]+)\*|`(?<code>[^`]+)`|~~(?<strike>[^~]+)~~|\[(?<linktext>[^\]]+)\]\([^\)]+\)", RegexOptions.Compiled)]
    private static partial Regex InlineRegex();

    [GeneratedRegex(@"^(\d+)\.\s", RegexOptions.Compiled)]
    private static partial Regex OrderedListRegex();
}
