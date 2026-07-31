using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GtaRpAssistant.App.DesignSystem.Controls;

/// <summary>
/// Renders a deliberately small, non-interactive Markdown subset. HTML and links
/// remain plain text so assistant output cannot execute content or open URLs.
/// </summary>
public sealed class MarkdownTextBlock : TextBlock
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, OnMarkdownChanged));

    public MarkdownTextBlock() => TextWrapping = TextWrapping.Wrap;

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value ?? "");
    }

    private static void OnMarkdownChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((MarkdownTextBlock)owner).Render(args.NewValue as string ?? "");

    private void Render(string value)
    {
        Inlines.Clear();
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            AddLine(lines[index]);
            if (index < lines.Length - 1) Inlines.Add(new LineBreak());
        }
    }

    private void AddLine(string source)
    {
        var line = source;
        var heading = 0;
        while (heading < line.Length && heading < 3 && line[heading] == '#') heading++;
        if (heading > 0 && heading < line.Length && line[heading] == ' ')
        {
            line = line[(heading + 1)..];
            Inlines.Add(new Run(line) { FontWeight = FontWeights.SemiBold, FontSize = FontSize + Math.Max(0, 3 - heading) });
            return;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            Inlines.Add(new Run("• ") { FontWeight = FontWeights.SemiBold });
            line = line[2..];
        }

        AddInlineFormatting(line);
    }

    private void AddInlineFormatting(string text)
    {
        var position = 0;
        while (position < text.Length)
        {
            if (text.AsSpan(position).StartsWith("**", StringComparison.Ordinal))
            {
                var end = text.IndexOf("**", position + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    Inlines.Add(new Run(text[(position + 2)..end]) { FontWeight = FontWeights.SemiBold });
                    position = end + 2;
                    continue;
                }
            }

            if (text[position] == '`')
            {
                var end = text.IndexOf('`', position + 1);
                if (end >= 0)
                {
                    Inlines.Add(new Run(text[(position + 1)..end])
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        Background = TryFindResource("Brush.Input") as System.Windows.Media.Brush,
                    });
                    position = end + 1;
                    continue;
                }
            }

            var nextBold = text.IndexOf("**", position, StringComparison.Ordinal);
            var nextCode = text.IndexOf('`', position);
            var next = new[] { nextBold, nextCode }.Where(x => x >= 0).DefaultIfEmpty(text.Length).Min();
            if (next == position) next++;
            Inlines.Add(new Run(text[position..next]));
            position = next;
        }
    }
}
