using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MailToMarkdownPlugin.Mail;

internal static class HtmlToMarkdown
{
    private static readonly Regex IgnoredBlocks = new(
        @"<(script|style|head|title)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Comments = new(
        "<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PreBlocks = new(
        @"<pre\b[^>]*>(.*?)</pre\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ImageTags = new(
        @"<img\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnchorTags = new(
        @"<a\b([^>]*)>(.*?)</a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnyTag = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    public static string Convert(string html, Func<string, string?>? cidResolver = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var preBlocks = new List<string>();
        var text = IgnoredBlocks.Replace(html, "");
        text = Comments.Replace(text, "");
        text = PreBlocks.Replace(text, match =>
        {
            preBlocks.Add(match.Groups[1].Value);
            return $"\u0001PRE{preBlocks.Count - 1}\u0001";
        });

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]*\n[ \t]*", " ");

        text = Regex.Replace(text, @"<br\b[^>]*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(p|div|section|article|header|footer|tr|table)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(p|div|section|article|header|footer|tr|table)\s*>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h([1-6])\b[^>]*>", match => "\n\n" + new string('#', int.Parse(match.Groups[1].Value)) + " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</li\s*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(ul|ol)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<blockquote\b[^>]*>", "\n> ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</blockquote\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<hr\b[^>]*/?>", "\n\n---\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(strong|b)\b[^>]*>", "**", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(strong|b)\s*>", "**", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(em|i)\b[^>]*>", "*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(em|i)\s*>", "*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<code\b[^>]*>", "`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</code\s*>", "`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<del\b[^>]*>", "~~", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</del\s*>", "~~", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(td|th)\b[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(td|th)\s*>", " | ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(sup|sub)\b[^>]*>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(sup|sub)\s*>", "", RegexOptions.IgnoreCase);

        text = ImageTags.Replace(text, match =>
        {
            var src = ResolveCid(Attribute(match.Value, "src"), cidResolver);
            if (string.IsNullOrWhiteSpace(src)) return "";
            var alt = Attribute(match.Value, "alt");
            return $"![{EscapeLinkText(alt ?? "")}]({src})";
        });

        text = AnchorTags.Replace(text, match =>
        {
            var href = ResolveCid(Attribute(match.Groups[1].Value, "href"), cidResolver);
            var label = WebUtility.HtmlDecode(AnyTag.Replace(match.Groups[2].Value, "")).Trim();
            if (string.IsNullOrWhiteSpace(href)) return label;
            if (label.Length == 0) label = href;
            return $"[{EscapeLinkText(label)}]({href})";
        });

        text = AnyTag.Replace(text, "");
        text = WebUtility.HtmlDecode(text);

        for (var i = 0; i < preBlocks.Count; i++)
        {
            var code = WebUtility.HtmlDecode(AnyTag.Replace(preBlocks[i], "")).Trim('\n');
            text = text.Replace($"\u0001PRE{i}\u0001", $"\n```\n{code}\n```\n");
        }

        return Normalize(text);
    }

    private static string? ResolveCid(string? value, Func<string, string?>? cidResolver)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) && cidResolver != null)
            return cidResolver(value[4..].Trim()) ?? value;

        return value;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(
            tag,
            @"\b" + Regex.Escape(name) + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        if (match.Groups[1].Success) return match.Groups[1].Value;
        if (match.Groups[2].Success) return match.Groups[2].Value;
        return match.Groups[3].Value;
    }

    private static string EscapeLinkText(string value) =>
        value.Replace("[", "\\[").Replace("]", "\\]");

    private static string Normalize(string value)
    {
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(value.Length);
        var blank = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Trim().Length == 0)
            {
                if (!blank && builder.Length > 0)
                {
                    builder.Append('\n');
                    blank = true;
                }
                continue;
            }

            builder.Append(line).Append('\n');
            blank = false;
        }

        return builder.ToString().Trim();
    }
}
