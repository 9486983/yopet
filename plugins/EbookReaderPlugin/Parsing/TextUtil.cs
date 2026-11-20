using System.Text;
using System.Text.RegularExpressions;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>文本清洗与切章工具。</summary>
public static class TextUtil
{
    private static readonly Regex ChineseChapterRegex = new(
        @"^[ \t]*(?:(?:第[0-9一二三四五六七八九十百千万零〇两]+[章节回卷部篇集])|(?:Chapter|CHAPTER|Part|PART)[ \t]+[0-9IVXLCivxlc]+|(?:序章|楔子|前言|后记|尾声|引子))(?:[ \t]|$|[：:、.．]).*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex StandaloneSectionRegex = new(
        @"^[ \t]*[一二三四五六七八九十百千万零〇两]+[、.．：:]?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownHeadingRegex = new(
        @"^#{1,4}[ \t]+.+$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex AuthorRegex = new(
        @"^\s*(?:作者|Author)\s*[：:]\s*(?<author>[^\r\n]{1,80})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var newlineRun = 0;

        foreach (var ch in text)
        {
            if (ch == '\r')
                continue;

            if (ch == '\n')
            {
                newlineRun++;
                if (newlineRun <= 2)
                    builder.Append('\n');
                continue;
            }

            newlineRun = 0;

            if (ch == '\t')
                builder.Append(' ');
            else if (!char.IsControl(ch) || ch == '\u00A0')
                builder.Append(ch == '\u00A0' ? ' ' : ch);
        }

        var lines = builder.ToString()
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();

        return string.Join('\n', lines).Trim();
    }

    public static string CleanInline(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : Regex.Replace(text, @"\s+", " ").Trim();

    public static string FileTitle(string path) =>
        Path.GetFileNameWithoutExtension(path).Trim();

    public static string TryGetAuthor(string text)
    {
        var match = AuthorRegex.Match(text.Length > 4096 ? text[..4096] : text);
        return match.Success ? CleanInline(match.Groups["author"].Value) : "";
    }

    public static string IndentParagraphs(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                lines[i] = "";
                continue;
            }

            lines[i] = "\u3000\u3000" + line.TrimStart(' ', '\t', '\u3000');
        }

        return string.Join('\n', lines);
    }

    public static List<Chapter> SplitChapters(string text, string fallbackTitle)
    {
        text = Normalize(text);
        if (text.Length == 0)
            return new List<Chapter>();

        var matches = new List<Match>();
        matches.AddRange(MarkdownHeadingRegex.Matches(text).Cast<Match>());
        matches.AddRange(ChineseChapterRegex.Matches(text).Cast<Match>());
        matches.AddRange(StandaloneSectionRegex.Matches(text).Cast<Match>());
        matches = matches
            .Where(match => match.Length > 0)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToList();

        if (matches.Count < 2)
        {
            if (text.Length <= 30000)
                return new List<Chapter> { new() { Title = fallbackTitle, Text = text } };

            return SplitLargeText(text, 15000);
        }

        var chapters = new List<Chapter>();

        var firstIndex = matches[0].Index;
        if (firstIndex > 0)
        {
            var preface = text[..firstIndex].Trim();
            if (preface.Length > 0)
                chapters.Add(new Chapter { Title = "前言", Text = preface });
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var nextIndex = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var bodyStart = match.Index + match.Length;
            if (bodyStart >= nextIndex)
                continue;

            while (bodyStart < nextIndex &&
                   (text[bodyStart] == '\n' || text[bodyStart] == '\r' ||
                    text[bodyStart] == ' ' || text[bodyStart] == '\t'))
                bodyStart++;

            var title = CleanInline(match.Value.TrimStart('#', ' ', '\t'));
            if (title.Length > 60)
                title = title[..60];

            var body = text[bodyStart..nextIndex].Trim();
            if (body.Length == 0)
                continue;

            chapters.Add(new Chapter
            {
                Title = string.IsNullOrWhiteSpace(title) ? $"第 {chapters.Count + 1} 节" : title,
                Text = body,
            });
        }

        if (chapters.Count == 0)
            chapters.Add(new Chapter { Title = fallbackTitle, Text = text });

        return chapters;
    }

    private static List<Chapter> SplitLargeText(string text, int targetLength)
    {
        var chapters = new List<Chapter>();
        var start = 0;
        var index = 1;

        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + targetLength);
            if (end < text.Length)
            {
                var boundary = text.LastIndexOf("\n\n", end, end - start, StringComparison.Ordinal);
                if (boundary < start + targetLength / 2)
                    boundary = text.LastIndexOf('\n', end - 1, end - start);
                if (boundary > start + targetLength / 2)
                    end = boundary;
            }

            var body = text[start..end].Trim();
            if (body.Length > 0)
                chapters.Add(new Chapter { Title = $"第 {index++} 部分", Text = body });

            start = end;
            while (start < text.Length && (text[start] == '\n' || text[start] == '\r' || text[start] == ' ' || text[start] == '\t'))
                start++;
        }

        return chapters;
    }
}
