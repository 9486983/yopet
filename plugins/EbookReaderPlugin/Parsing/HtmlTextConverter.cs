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

    public static (string Title, List<Chapter> Chapters) BuildFromHtml(
        string html,
        string fallbackTitle,
        string? preferredTitle = null)
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

            return (title, chapters);
        }

        var parentTitle = FirstNonEmpty(preferredTitle, htmlTitle);
        var first = headings[0];
        var preface = ToPlainText(html[..first.Start]);
        if (preface.Length > 120)
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

        return (title, chapters);
    }

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
        var title = TextUtil.CleanInline(ToPlainText(html));
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
