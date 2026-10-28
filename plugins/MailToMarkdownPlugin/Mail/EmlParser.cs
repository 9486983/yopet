using System.Globalization;
using System.Text;

namespace MailToMarkdownPlugin.Mail;

internal static class EmlParser
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private sealed class Entity
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? PlainBody { get; set; }
        public string? HtmlBody { get; set; }
        public List<MailAttachment> Attachments { get; } = new();
    }

    public static MailDocument Parse(string path) => Parse(File.ReadAllBytes(path));

    internal static MailDocument Parse(byte[] data)
    {
        var entity = ParseEntity(data);
        var document = new MailDocument
        {
            Subject = NullIfEmpty(Header(entity, "subject")),
            From = NullIfEmpty(Header(entity, "from")),
            To = NullIfEmpty(Header(entity, "to")),
            Cc = NullIfEmpty(Header(entity, "cc")),
            PlainBody = NullIfEmpty(entity.PlainBody),
            HtmlBody = NullIfEmpty(entity.HtmlBody),
        };

        var date = Header(entity, "date");
        if (!string.IsNullOrWhiteSpace(date))
        {
            document.DateRaw = date;
            document.Date = ParseDate(date);
        }

        document.Attachments.AddRange(entity.Attachments);
        return document;
    }

    private static Entity ParseEntity(byte[] content)
    {
        var (headerText, body) = SplitHeaderAndBody(content);
        var headers = ParseHeaders(headerText);

        var entity = new Entity();
        foreach (var (key, value) in headers)
            entity.Headers[key] = value;

        var (mediaType, contentTypeParameters) = MimeHelpers.ParseParameters(Header(entity, "content-type"));
        mediaType = string.IsNullOrWhiteSpace(mediaType) ? "text/plain" : mediaType.Trim().ToLowerInvariant();

        var (disposition, dispositionParameters) = MimeHelpers.ParseParameters(Header(entity, "content-disposition"));
        disposition = disposition?.Trim().ToLowerInvariant() ?? "";

        if (mediaType.StartsWith("multipart/", StringComparison.Ordinal))
        {
            var boundary = contentTypeParameters.GetValueOrDefault("boundary");
            if (!string.IsNullOrWhiteSpace(boundary))
            {
                foreach (var part in SplitMultipart(body, boundary))
                {
                    var child = ParseEntity(part);
                    Merge(entity, child);
                }
            }
            return entity;
        }

        var transferEncoding = Header(entity, "content-transfer-encoding")?.Trim().ToLowerInvariant();
        var decodedBody = DecodeTransfer(body, transferEncoding);
        var charset = contentTypeParameters.GetValueOrDefault("charset");
        var contentId = Header(entity, "content-id")?.Trim().Trim('<', '>');
        var fileName = dispositionParameters.GetValueOrDefault("filename") ??
                       contentTypeParameters.GetValueOrDefault("name");

        if (disposition == "attachment" || !string.IsNullOrWhiteSpace(fileName))
        {
            var attachment = new MailAttachment
            {
                FileName = EnsureFileName(fileName, mediaType, contentId, entity.Attachments.Count),
                ContentId = NullIfEmpty(contentId),
                MimeType = mediaType,
                Data = decodedBody,
                IsInline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase),
            };
            MimeHelpers.ApplySniffedType(attachment);
            entity.Attachments.Add(attachment);
            return entity;
        }

        switch (mediaType)
        {
            case "text/plain":
                entity.PlainBody = NormalizeText(MimeHelpers.DecodeBytes(decodedBody, charset));
                break;
            case "text/html":
                entity.HtmlBody = NormalizeText(MimeHelpers.DecodeBytes(decodedBody, charset));
                break;
            case "message/rfc822":
            {
                var attachment = new MailAttachment
                {
                    FileName = EnsureFileName(fileName, mediaType, contentId, entity.Attachments.Count, ".eml"),
                    ContentId = NullIfEmpty(contentId),
                    MimeType = mediaType,
                    Data = decodedBody,
                };
                MimeHelpers.ApplySniffedType(attachment);
                entity.Attachments.Add(attachment);
                break;
            }
            default:
                if (!string.IsNullOrWhiteSpace(contentId) || mediaType.StartsWith("image/", StringComparison.Ordinal))
                {
                    var attachment = new MailAttachment
                    {
                        FileName = EnsureFileName(fileName, mediaType, contentId, entity.Attachments.Count),
                        ContentId = NullIfEmpty(contentId),
                        MimeType = mediaType,
                        Data = decodedBody,
                        IsInline = true,
                    };
                    MimeHelpers.ApplySniffedType(attachment);
                    entity.Attachments.Add(attachment);
                }
                break;
        }

        return entity;
    }

    private static void Merge(Entity target, Entity child)
    {
        if (string.IsNullOrWhiteSpace(target.PlainBody) && !string.IsNullOrWhiteSpace(child.PlainBody))
            target.PlainBody = child.PlainBody;
        if (string.IsNullOrWhiteSpace(target.HtmlBody) && !string.IsNullOrWhiteSpace(child.HtmlBody))
            target.HtmlBody = child.HtmlBody;
        target.Attachments.AddRange(child.Attachments);
    }

    private static (string HeaderText, byte[] Body) SplitHeaderAndBody(byte[] content)
    {
        for (var i = 0; i + 3 < content.Length; i++)
        {
            if (content[i] == (byte)'\r' && content[i + 1] == (byte)'\n' &&
                content[i + 2] == (byte)'\r' && content[i + 3] == (byte)'\n')
            {
                return (Latin1.GetString(content, 0, i), content[(i + 4)..]);
            }
        }

        for (var i = 0; i + 1 < content.Length; i++)
        {
            if (content[i] == (byte)'\n' && content[i + 1] == (byte)'\n')
                return (Latin1.GetString(content, 0, i), content[(i + 2)..]);
        }

        return (Latin1.GetString(content), Array.Empty<byte>());
    }

    private static Dictionary<string, string> ParseHeaders(string headerBlock)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = headerBlock.Split('\n');
        string? currentName = null;
        var currentValue = new StringBuilder();

        void Flush()
        {
            if (currentName == null) return;
            var value = MimeHelpers.DecodeHeaderValue(currentValue.ToString());
            if (!result.ContainsKey(currentName)) result[currentName] = value;
            currentName = null;
            currentValue.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            if ((line[0] == ' ' || line[0] == '\t') && currentName != null)
            {
                currentValue.Append(' ').Append(line.Trim());
                continue;
            }

            Flush();

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            currentName = line[..colon].Trim();
            currentValue.Append(line[(colon + 1)..].Trim());
        }

        Flush();
        return result;
    }

    private static IEnumerable<byte[]> SplitMultipart(byte[] body, string boundary)
    {
        var text = Latin1.GetString(body);
        var marker = "--" + boundary;
        var results = new List<byte[]>();
        var searchFrom = 0;
        var partStart = -1;
        var closed = false;

        while (searchFrom < text.Length)
        {
            var index = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;

            if (index > 0 && text[index - 1] != '\n')
            {
                searchFrom = index + marker.Length;
                continue;
            }

            if (partStart >= 0)
            {
                var length = index - partStart;
                var part = text.Substring(partStart, length).TrimEnd('\r', '\n');
                results.Add(Latin1.GetBytes(part));
            }

            var afterMarker = index + marker.Length;
            if (afterMarker + 1 < text.Length && text[afterMarker] == '-' && text[afterMarker + 1] == '-')
            {
                closed = true;
                break;
            }

            var lineEnd = text.IndexOf('\n', afterMarker);
            if (lineEnd < 0) break;

            partStart = lineEnd + 1;
            searchFrom = partStart;
        }

        if (!closed && partStart >= 0 && partStart < text.Length)
        {
            var part = text[partStart..].TrimEnd('\r', '\n');
            results.Add(Latin1.GetBytes(part));
        }

        return results;
    }

    private static byte[] DecodeTransfer(byte[] body, string? transferEncoding) => transferEncoding switch
    {
        "base64" => MimeHelpers.DecodeBase64(Latin1.GetString(body)),
        "quoted-printable" => MimeHelpers.DecodeQuotedPrintable(body),
        _ => body,
    };

    private static string EnsureFileName(string? fileName, string mediaType, string? contentId, int index, string? fallbackExtension = null)
    {
        var name = fileName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(contentId)
                ? $"attachment_{index + 1}"
                : $"inline_{index + 1}";
            name += fallbackExtension ?? MimeHelpers.ExtensionForMediaType(mediaType);
        }

        return name;
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        var text = value.Trim();
        var comment = text.IndexOf('(');
        if (comment > 0) text = text[..comment].Trim();

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed;

        return null;
    }

    private static string? Header(Entity entity, string name) =>
        entity.Headers.TryGetValue(name, out var value) ? value : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeText(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\0', '\n', '\r');
}
