using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace EbookReaderPlugin.Views;

/// <summary>一个分页片段：页面文本及其在章节中的字符区间。</summary>
public sealed class PageTextSlice
{
    public string Text { get; init; } = "";

    public int CharStart { get; init; }

    public int CharEnd { get; init; }
}

/// <summary>基于 Avalonia TextLayout 行信息的分页器，保证每页文本恰好填满可视高度。</summary>
public static class PaginationEngine
{
    public static List<PageTextSlice> Paginate(
        string text,
        double width,
        double height,
        FontFamily? fontFamily,
        double fontSize,
        double lineSpacing,
        double firstPageHeaderHeight = 0)
    {
        if (string.IsNullOrEmpty(text))
            return new List<PageTextSlice> { new() { Text = "", CharStart = 0, CharEnd = 0 } };

        width = Math.Max(width, 40);
        height = Math.Max(height, 40);
        fontSize = Math.Clamp(fontSize, 8, 72);
        lineSpacing = Math.Clamp(lineSpacing, 1.0, 3.0);

        var lineHeight = fontSize * lineSpacing;
        var family = fontFamily ?? FontFamily.Default;
        var layout = CreateLayout(text, family, fontSize, width, lineHeight);
        var lines = layout.TextLines;
        if (lines.Count == 0)
            return new List<PageTextSlice> { new() { Text = text, CharStart = 0, CharEnd = text.Length } };

        var pages = new List<PageTextSlice>();
        var startLine = 0;
        var usedHeight = 0.0;
        var available = Math.Max(height - 2, lineHeight);
        var firstAvailable = firstPageHeaderHeight > 0
            ? Math.Max(height - firstPageHeaderHeight - 2, lineHeight)
            : available;
        var currentAvailable = firstAvailable;

        for (var i = 0; i < lines.Count; i++)
        {
            var currentHeight = lines[i].Height;
            if (currentHeight <= 0)
                currentHeight = lineHeight;

            if (usedHeight + currentHeight > currentAvailable && i > startLine)
            {
                AddPage(pages, text, lines, startLine, i);
                startLine = i;
                usedHeight = 0;
                currentAvailable = available;
            }

            usedHeight += currentHeight;
        }

        if (startLine < lines.Count)
            AddPage(pages, text, lines, startLine, lines.Count);

        if (pages.Count == 0)
            pages.Add(new PageTextSlice { Text = text, CharStart = 0, CharEnd = text.Length });

        return pages;
    }

    private static TextLayout CreateLayout(
        string text,
        FontFamily family,
        double fontSize,
        double width,
        double lineHeight)
    {
        try
        {
            return new TextLayout(
                text,
                new Typeface(family),
                fontSize,
                Brushes.Black,
                TextAlignment.Left,
                TextWrapping.Wrap,
                TextTrimming.None,
                null,
                FlowDirection.LeftToRight,
                maxWidth: width,
                maxHeight: double.PositiveInfinity,
                lineHeight: lineHeight);
        }
        catch
        {
            return new TextLayout(
                text,
                new Typeface(FontFamily.Default),
                fontSize,
                Brushes.Black,
                TextAlignment.Left,
                TextWrapping.Wrap,
                TextTrimming.None,
                null,
                FlowDirection.LeftToRight,
                maxWidth: width,
                maxHeight: double.PositiveInfinity,
                lineHeight: lineHeight);
        }
    }

    private static void AddPage(
        List<PageTextSlice> pages,
        string text,
        IReadOnlyList<TextLine> lines,
        int startLine,
        int endLine)
    {
        var start = lines[startLine].FirstTextSourceIndex;
        var end = endLine >= lines.Count
            ? text.Length
            : lines[endLine].FirstTextSourceIndex;
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);

        var slice = text[start..end].TrimEnd('\n', '\r');
        if (slice.Length == 0 && pages.Count > 0)
            return;

        pages.Add(new PageTextSlice
        {
            Text = slice,
            CharStart = start,
            CharEnd = end,
        });
    }
}
