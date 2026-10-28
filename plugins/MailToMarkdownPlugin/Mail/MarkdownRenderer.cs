using System.Text;
using System.Text.RegularExpressions;

namespace MailToMarkdownPlugin.Mail;

internal sealed class MarkdownLabels
{
    public string From { get; init; } = "From";
    public string To { get; init; } = "To";
    public string Cc { get; init; } = "Cc";
    public string Date { get; init; } = "Date";
    public string EmptyBody { get; init; } = "";
    public string Attachments { get; init; } = "Attachments";
}

internal static class MarkdownRenderer
{
    private static readonly Regex PlainContentId = new(
        @"\[cid:([^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FileLabelLink = new(
        @"(?<label>[A-Za-z0-9][A-Za-z0-9 ._\-]{0,120}?\.(?:xlsx|xlsm|xls|docx|doc|pdf|pptx|ppt|txt|csv|tsv|zip|rar|7z|png|jpe?g|gif|bmp|webp|eml|msg|ics|html?))<(?<url>https?://[^>\s]+)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TokenLabelLink = new(
        @"(?<label>[^\s<>]{1,120})<(?<url>https?://[^>\s]+)>",
        RegexOptions.Compiled);

    private static readonly Regex AngleLink = new(
        @"<(?<url>https?://[^>\s]+)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageTag = new(
        @"!\[[^\]]*\]\([^)]*\)",
        RegexOptions.Compiled);

    private static readonly Regex ImagePlaceholder = new(
        @"(?<!!)\[image\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberedItem = new(
        @"^(?<indent>[ \t]*)(?<number>[0-9]{1,3})[、．]\s*(?<text>.*)$",
        RegexOptions.Compiled);

    public static string Render(
        MailDocument document,
        string fallbackSubject,
        IReadOnlyList<string?> attachmentLinks,
        MarkdownLabels labels)
    {
        var cidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Attachments.Count && i < attachmentLinks.Count; i++)
        {
            var contentId = document.Attachments[i].ContentId;
            var link = attachmentLinks[i];
            if (!string.IsNullOrWhiteSpace(contentId) && link != null)
                cidMap[contentId] = link;
        }

        var subject = string.IsNullOrWhiteSpace(document.Subject)
            ? fallbackSubject
            : document.Subject;

        var builder = new StringBuilder();
        builder.Append("# ").Append(EscapeHeading(subject)).Append("\n\n");

        var hasMetadata = false;
        AppendMetadata(builder, labels.From, document.From, ref hasMetadata);
        AppendMetadata(builder, labels.To, document.To, ref hasMetadata);
        AppendMetadata(builder, labels.Cc, document.Cc, ref hasMetadata);
        AppendMetadata(builder, labels.Date, FormatDate(document), ref hasMetadata);

        if (hasMetadata) builder.Append('\n');

        builder.Append(SelectBody(document, labels, cidMap, attachmentLinks).Trim());

        if (document.Attachments.Count > 0)
        {
            builder.Append("\n\n## ").Append(labels.Attachments).Append("\n\n");
            for (var i = 0; i < document.Attachments.Count; i++)
            {
                var attachment = document.Attachments[i];
                var name = EscapeLinkText(attachment.FileName);
                var size = FormatSize(attachment.Data.LongLength);
                var link = i < attachmentLinks.Count ? attachmentLinks[i] : null;

                builder.Append("- ");
                if (link == null)
                    builder.Append(name);
                else
                    builder.Append('[').Append(name).Append("](").Append(link).Append(')');
                builder.Append(" (").Append(size).Append(")\n");
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    private static string SelectBody(
        MailDocument document,
        MarkdownLabels labels,
        Dictionary<string, string> cidMap,
        IReadOnlyList<string?> attachmentLinks)
    {
        if (!string.IsNullOrWhiteSpace(document.PlainBody))
        {
            var body = NormalizeBody(document.PlainBody);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in PlainContentId.Matches(body))
                referenced.Add(match.Groups[1].Value.Trim());

            body = ReplacePlainContentIds(body, cidMap);
            body = ReplaceImagePlaceholders(body, document, attachmentLinks, referenced);
            body = ConvertPlainLinks(body);
            body = ConvertNumberedItems(body);
            return FinalizeBlocks(body);
        }

        if (!string.IsNullOrWhiteSpace(document.HtmlBody))
        {
            string? ResolveCid(string cid) => cidMap.GetValueOrDefault(cid);
            var converted = HtmlToMarkdown.Convert(document.HtmlBody, ResolveCid);
            if (!string.IsNullOrWhiteSpace(converted)) return FinalizeBlocks(converted);
        }

        return labels.EmptyBody;
    }

    private static void AppendMetadata(StringBuilder builder, string label, string? value, ref bool hasMetadata)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        builder.Append("- **").Append(label).Append("**: ").Append(value.Trim()).Append('\n');
        hasMetadata = true;
    }

    private static string? FormatDate(MailDocument document)
    {
        if (document.Date.HasValue)
            return document.Date.Value.ToString("yyyy-MM-dd HH:mm:ss zzz");

        return document.DateRaw;
    }

    private static string ReplacePlainContentIds(string body, Dictionary<string, string> cidMap)
    {
        if (cidMap.Count == 0) return body;

        return PlainContentId.Replace(body, match =>
        {
            var contentId = match.Groups[1].Value.Trim();
            return cidMap.TryGetValue(contentId, out var link) ? $"![image]({link})" : match.Value;
        });
    }

    private static string ReplaceImagePlaceholders(
        string body,
        MailDocument document,
        IReadOnlyList<string?> attachmentLinks,
        HashSet<string> referenced)
    {
        if (!ImagePlaceholder.IsMatch(body)) return body;

        var available = new List<string>();
        for (var i = 0; i < document.Attachments.Count && i < attachmentLinks.Count; i++)
        {
            var attachment = document.Attachments[i];
            var link = attachmentLinks[i];
            if (link == null) continue;
            if (!attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!attachment.IsInline && string.IsNullOrWhiteSpace(attachment.ContentId)) continue;
            if (!string.IsNullOrWhiteSpace(attachment.ContentId) && referenced.Contains(attachment.ContentId)) continue;
            available.Add(link);
        }

        var index = 0;
        return ImagePlaceholder.Replace(body, _ =>
            index < available.Count ? $"![image]({available[index++]})" : "*[image]*");
    }

    private static string ConvertNumberedItems(string body)
    {
        var lines = body.Split('\n');
        var builder = new StringBuilder(body.Length + 8);
        var previousBlank = true;
        var previousNumbered = false;

        foreach (var rawLine in lines)
        {
            var match = NumberedItem.Match(rawLine);
            if (match.Success)
            {
                if (!previousBlank && !previousNumbered && builder.Length > 0)
                    builder.Append('\n');

                builder.Append(match.Groups["indent"].Value)
                    .Append(match.Groups["number"].Value)
                    .Append(". ")
                    .Append(match.Groups["text"].Value)
                    .Append('\n');

                previousBlank = false;
                previousNumbered = true;
                continue;
            }

            builder.Append(rawLine).Append('\n');
            previousBlank = rawLine.Trim().Length == 0;
            previousNumbered = false;
        }

        return builder.ToString();
    }

    private static string ConvertPlainLinks(string body)
    {
        body = FileLabelLink.Replace(body, match =>
            MarkdownLink(match.Groups["label"].Value, match.Groups["url"].Value));
        body = TokenLabelLink.Replace(body, match =>
            MarkdownLink(match.Groups["label"].Value, match.Groups["url"].Value));
        body = AngleLink.Replace(body, match =>
            MarkdownLink(match.Groups["url"].Value, match.Groups["url"].Value));
        return body;
    }

    private static string MarkdownLink(string label, string url)
    {
        label = label.Trim().Trim('\u200B', '\uFEFF');
        url = url.Trim().Trim('\u200B', '\uFEFF');
        if (label.Length == 0) label = url;

        var destination = url.IndexOfAny(new[] { ' ', '\t', '(', ')', '<', '>' }) >= 0
            ? $"<{url}>"
            : url;
        return $"[{EscapeLinkText(label)}]({destination})";
    }

    private static string FinalizeBlocks(string body)
    {
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(body.Length + 16);
        var blank = true;
        var inFence = false;

        foreach (var rawLine in lines)
        {
            if (rawLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence && !blank && builder.Length > 0) builder.Append('\n');
                builder.Append(rawLine.TrimEnd()).Append('\n');
                inFence = !inFence;
                blank = false;
                continue;
            }

            if (inFence)
            {
                builder.Append(rawLine).Append('\n');
                continue;
            }

            var line = rawLine.TrimEnd();
            if (line.Trim().Length == 0)
            {
                if (!blank && builder.Length > 0) builder.Append('\n');
                blank = true;
                continue;
            }

            var trimmed = line.Trim();
            var images = ImageTag.Matches(trimmed);
            if (images.Count > 0 && ImageTag.Replace(trimmed, "").Trim().Length == 0)
            {
                if (!blank && builder.Length > 0) builder.Append('\n');
                foreach (Match image in images)
                    builder.Append(image.Value).Append("\n\n");
                blank = true;
                continue;
            }

            builder.Append(line).Append('\n');
            blank = false;
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeBody(string body) =>
        body.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("\u200B", "")
            .Replace("\uFEFF", "")
            .Trim();

    private static string EscapeHeading(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        }
        return builder.ToString().Trim();
    }

    private static string EscapeLinkText(string value) =>
        value.Replace("[", "\\[").Replace("]", "\\]");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
