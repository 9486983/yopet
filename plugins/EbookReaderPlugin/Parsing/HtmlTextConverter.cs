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
        "<h(?<level>[12])[^>]*>(?<title>.*?)</h\\k<level>>",
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

    public static (string Title, List<Chapter> Chapters) BuildFromHtml(string html, string fallbackTitle)
    {
        var title = ExtractTitle(html);
        if (string.IsNullOrWhiteSpace(title))
            title = fallbackTitle;

        var chapters = new List<Chapter>();
        var headings = HeadingRegex.Matches(html).Cast<Match>().ToList();

        if (headings.Count > 0)
        {
            var prefaceEnd = headings[0].Index;
            if (prefaceEnd > 0)
            {
                var preface = ToPlainText(html[..prefaceEnd]);
                if (preface.Length > 120)
                    chapters.Add(new Chapter { Title = "前言", Text = preface });
            }

            for (var i = 0; i < headings.Count; i++)
            {
                var heading = headings[i];
                var bodyStart = heading.Index + heading.Length;
                var bodyEnd = i + 1 < headings.Count ? headings[i + 1].Index : html.Length;
                if (bodyEnd <= bodyStart)
                    continue;

                var body = ToPlainText(html[bodyStart..bodyEnd]);
                if (body.Length == 0)
                    continue;

                var chapterTitle = ToPlainText(heading.Groups["title"].Value);
                chapterTitle = TextUtil.CleanInline(chapterTitle);
                if (chapterTitle.Length > 80)
                    chapterTitle = chapterTitle[..80];

                chapters.Add(new Chapter
                {
                    Title = string.IsNullOrWhiteSpace(chapterTitle)
                        ? $"第 {chapters.Count + 1} 章"
                        : chapterTitle,
                    Text = body,
                });
            }
        }

        if (chapters.Count == 0)
        {
            var text = ToPlainText(html);
            if (text.Length > 0)
                chapters.Add(new Chapter { Title = fallbackTitle, Text = text });
        }

        return (title, chapters);
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
}
