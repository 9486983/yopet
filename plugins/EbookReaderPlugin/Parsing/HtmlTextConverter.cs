using System.Net;
using System.Text.RegularExpressions;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>把 XHTML / HTML / MOBI 内嵌 HTML 转成可重排的纯文本章节。</summary>
public static class HtmlTextConverter
{
    private static readonly Regex CommentRegex = new(
        "<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HeadRegex = new(
        "<(script|style|head)[^>]*>.*?</\\1>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BreakRegex = new(
        "<br\\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PageBreakRegex = new(
        "<mbp:pagebreak[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockEndRegex = new(
        "</(?:p|div|h[1-6]|li|tr|blockquote|section|article|header|footer|dd|dt|pre)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TitleRegex = new(
        "<title[^>]*>(?<title>.*?)</title>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex HeadingRegex = new(
        "<h(?<level>[1-6])[^>]*>(?<title>.*?)</h\\k<level>>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex ImageTagRegex = new(
        "<(?:img|image)\\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex ImageSourceRegex = new(
        @"(?:\s+src|\s+xlink:href|\s+href)\s*=\s*(?<quote>[""'])(?<src>.*?)\k<quote>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = CommentRegex.Replace(html, " ");
        text = HeadRegex.Replace(text, " ");
        text = PageBreakRegex.Replace(text, "\n\n");
        text = BreakRegex.Replace(text, "\n");
        text = BlockEndRegex.Replace(text, "\n");
        text = TagRegex.Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        return TextUtil.Normalize(text);
    }

    public static (string Html, List<BookImage> Images) ExtractImages(
        string html,
        Func<string, BookImage?> resolve)
    {
        var images = new List<BookImage>();
        var ignoredRanges = new List<(int Start, int End)>();
        AddRanges(CommentRegex, html, ignoredRanges);
        AddRanges(HeadRegex, html, ignoredRanges);
        foreach (Match heading in HeadingRegex.Matches(html))
        {
            if (int.Parse(heading.Groups["level"].Value) <= 3)
                ignoredRanges.Add((heading.Index, heading.Index + heading.Length));
        }

        var prepared = ImageTagRegex.Replace(html, match =>
        {
            if (IsInside(match.Index, ignoredRanges))
                return "";

            var source = ImageSourceRegex.Match(match.Value);
            if (!source.Success)
                return "";

            var image = resolve(WebUtility.HtmlDecode(source.Groups["src"].Value));
            if (image is null)
                return "";

            images.Add(image);
            return BookImage.Placeholder.ToString();
        });

        return (prepared, images);
    }

    private static void AddRanges(Regex regex, string text, List<(int Start, int End)> ranges)
    {
        foreach (Match match in regex.Matches(text))
            ranges.Add((match.Index, match.Index + match.Length));
    }

    private static bool IsInside(int index, List<(int Start, int End)> ranges)
    {
        foreach (var (start, end) in ranges)
        {
            if (index >= start && index < end)
                return true;
        }

        return false;
    }

    public static (string Title, List<Chapter> Chapters) BuildFromHtml(
        string html,
        string fallbackTitle,
        string? preferredTitle = null,
        IReadOnlyList<BookImage>? images = null)
    {
        var htmlTitle = ExtractTitle(html);
        var title = FirstNonEmpty(preferredTitle, htmlTitle, fallbackTitle);
        var chapters = new List<Chapter>();
        var headings = HeadingRegex.Matches(html)
            .Cast<Match>()
            .Select(match => new
            {
                Start = match.Index,
                End = match.Index + match.Length,
                Level = int.Parse(match.Groups["level"].Value),
                Title = CleanHeading(match.Groups["title"].Value),
            })
            .Where(heading => heading.Level <= 3 && heading.Title.Length > 0)
            .ToList();

        if (headings.Count == 0)
        {
            var text = ToPlainText(html);
            if (text.Length > 0)
                chapters.Add(new Chapter { Title = title, Text = text });

            AttachImages(chapters, images);
            return (title, chapters);
        }

        var parentTitle = FirstNonEmpty(preferredTitle, htmlTitle);
        var first = headings[0];
        var preface = ToPlainText(html[..first.Start]);
        if (preface.Length > 120 || ContainsPlaceholder(preface))
            chapters.Add(new Chapter { Title = "前言", Text = preface });

        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            if (heading.Level <= 2)
                parentTitle = heading.Title;

            var bodyStart = heading.End;
            var bodyEnd = i + 1 < headings.Count ? headings[i + 1].Start : html.Length;
            if (bodyEnd <= bodyStart)
                continue;

            var body = ToPlainText(html[bodyStart..bodyEnd]);
            if (body.Length == 0)
                continue;

            var chapterTitle = heading.Level >= 3
                ? CombineTitles(parentTitle, heading.Title)
                : heading.Title;

            chapters.Add(new Chapter
            {
                Title = TrimTitle(chapterTitle),
                Text = body,
            });
        }

        if (chapters.Count == 0)
        {
            var text = ToPlainText(html);
            if (text.Length > 0)
                chapters.Add(new Chapter { Title = title, Text = text });
        }

        AttachImages(chapters, images);
        return (title, chapters);
    }

    private static void AttachImages(List<Chapter> chapters, IReadOnlyList<BookImage>? images)
    {
        if (images is not { Count: > 0 })
            return;

        var imageIndex = 0;
        foreach (var chapter in chapters)
        {
            for (var i = 0; i < chapter.Text.Length && imageIndex < images.Count; i++)
            {
                if (chapter.Text[i] == BookImage.Placeholder)
                    chapter.Images.Add(images[imageIndex++]);
            }
        }
    }

    private static bool ContainsPlaceholder(string text) =>
        text.IndexOf(BookImage.Placeholder) >= 0;

    public static string ExtractStoryTitle(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        foreach (Match match in HeadingRegex.Matches(html))
        {
            if (int.Parse(match.Groups["level"].Value) > 2)
                continue;

            var title = CleanHeading(match.Groups["title"].Value);
            if (title.Length > 0)
                return title;
        }

        return "";
    }

    public static bool HasSectionHeading(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        foreach (Match match in HeadingRegex.Matches(html))
        {
            if (int.Parse(match.Groups["level"].Value) == 3 &&
                CleanHeading(match.Groups["title"].Value).Length > 0)
                return true;
        }

        return false;
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex.Match(html);
        if (!match.Success)
            return "";

        var title = ToPlainText(match.Groups["title"].Value);
        title = TextUtil.CleanInline(title);
        return title.Length > 120 ? title[..120] : title;
    }

    private static string CleanHeading(string? html)
    {
        var title = TextUtil.CleanInline(ToPlainText(html))
            .Replace(BookImage.Placeholder.ToString(), "");
        return TrimTitle(title);
    }

    private static string CombineTitles(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent))
            return child;
        if (string.IsNullOrWhiteSpace(child))
            return parent;
        if (parent.Equals(child, StringComparison.OrdinalIgnoreCase))
            return parent;
        if (child.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            return child;

        return $"{parent} · {child}";
    }

    private static string TrimTitle(string title) =>
        title.Length > 100 ? title[..100] : title;

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }
}
