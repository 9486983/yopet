using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MailToMarkdownPlugin.Mail;

internal static class MsgParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MailDocument Parse(string path)
    {
        var file = new CompoundFile(path);
        var codePage = ReadInt32(file, "3FDE0003") ?? 0;
        var document = new MailDocument
        {
            Subject = Clean(ReadString(file, "0037", codePage)),
            From = ComposeAddress(Clean(ReadString(file, "0C1A", codePage)), Clean(ReadString(file, "0C1F", codePage))),
            To = Clean(ReadString(file, "0E04", codePage)),
            Cc = Clean(ReadString(file, "0E03", codePage)),
            PlainBody = Clean(ReadString(file, "1000", codePage)),
            HtmlBody = Clean(ReadHtml(file, codePage)),
        };

        document.Date = ReadFileTime(file, "00390040") ?? ReadFileTime(file, "0E060040");

        foreach (var storage in file.GetRootStorages("__attach_version1.0_")
                     .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var streams = file.GetStorageStreams(storage);
            if (!streams.TryGetValue("__substg1.0_37010102", out var dataEntry))
                continue;

            var data = file.ReadStream(dataEntry);
            var fileName = Clean(ReadStorageString(file, streams, "3707", codePage)) ??
                           Clean(ReadStorageString(file, streams, "3704", codePage));
            var extension = Clean(ReadStorageString(file, streams, "3703", codePage));
            var contentId = Clean(ReadStorageString(file, streams, "3712", codePage))?.Trim('<', '>');
            var mimeType = Clean(ReadStorageString(file, streams, "370E", codePage)) ?? "application/octet-stream";

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"attachment_{document.Attachments.Count + 1}";
                if (!string.IsNullOrWhiteSpace(extension))
                    fileName += extension.StartsWith('.') ? extension : "." + extension;
                else
                    fileName += MimeHelpers.ExtensionForMediaType(mimeType);
            }

            var attachment = new MailAttachment
            {
                FileName = fileName,
                ContentId = contentId,
                MimeType = mimeType,
                Data = data,
                IsInline = !string.IsNullOrWhiteSpace(contentId),
            };
            MimeHelpers.ApplySniffedType(attachment);
            document.Attachments.Add(attachment);
        }

        return document;
    }

    private static string? ReadString(CompoundFile file, string propertyId, int codePage)
    {
        var unicode = file.GetRootStream(PropName(propertyId, "001F"));
        if (unicode != null) return Encoding.Unicode.GetString(unicode);

        var ansi = file.GetRootStream(PropName(propertyId, "001E"));
        if (ansi != null) return DecodeAnsi(ansi, codePage);

        return null;
    }

    private static string? ReadStorageString(CompoundFile file, Dictionary<string, DirEntry> streams, string propertyId, int codePage)
    {
        if (streams.TryGetValue(PropName(propertyId, "001F"), out var unicode))
            return Encoding.Unicode.GetString(file.ReadStream(unicode));

        if (streams.TryGetValue(PropName(propertyId, "001E"), out var ansi))
            return DecodeAnsi(file.ReadStream(ansi), codePage);

        return null;
    }

    private static string? ReadHtml(CompoundFile file, int codePage)
    {
        var binary = file.GetRootStream(PropName("1013", "0102"));
        if (binary != null) return DecodeHtml(binary, codePage);

        var unicode = file.GetRootStream(PropName("1013", "001F"));
        if (unicode != null) return Encoding.Unicode.GetString(unicode);

        var ansi = file.GetRootStream(PropName("1013", "001E"));
        if (ansi != null) return DecodeAnsi(ansi, codePage);

        return null;
    }

    private static int? ReadInt32(CompoundFile file, string propertyName)
    {
        var data = file.GetRootStream("__substg1.0_" + propertyName);
        if (data is not { Length: >= 4 }) return null;
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    private static DateTimeOffset? ReadFileTime(CompoundFile file, string propertyName)
    {
        var data = file.GetRootStream("__substg1.0_" + propertyName);
        if (data is not { Length: >= 8 }) return null;

        var value = BinaryPrimitives.ReadInt64LittleEndian(data);
        if (value <= 0) return null;

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(value), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeAnsi(byte[] data, int codePage)
    {
        var charset = codePage > 0 ? codePage.ToString(CultureInfo.InvariantCulture) : null;
        return MimeHelpers.DecodeBytes(data, charset);
    }

    private static string DecodeHtml(byte[] data, int codePage)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);

        try
        {
            return StrictUtf8.GetString(data);
        }
        catch
        {
        }

        var declared = FindMetaCharset(data);
        if (declared != null)
        {
            var decoded = MimeHelpers.DecodeBytes(data, declared);
            if (!string.IsNullOrWhiteSpace(decoded)) return decoded;
        }

        return MimeHelpers.DecodeBytes(data, codePage > 0 ? codePage.ToString(CultureInfo.InvariantCulture) : null);
    }

    private static string? FindMetaCharset(byte[] data)
    {
        var head = Encoding.Latin1.GetString(data, 0, Math.Min(data.Length, 4096));
        var marker = head.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;

        var start = marker + "charset=".Length;
        while (start < head.Length && (head[start] == '"' || head[start] == '\'' || head[start] == ' '))
            start++;

        var end = start;
        while (end < head.Length && (char.IsLetterOrDigit(head[end]) || head[end] is '-' or '_' or '.'))
            end++;

        return end > start ? head[start..end] : null;
    }

    private static string PropName(string propertyId, string propertyType) =>
        $"__substg1.0_{propertyId}{propertyType}";

    private static string? ComposeAddress(string? name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name)) return email;
        if (string.IsNullOrWhiteSpace(email)) return name;
        if (name.Contains(email, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} <{email}>";
    }

    private static string? Clean(string? value)
    {
        if (value == null) return null;
        var cleaned = value.TrimEnd('\0').Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

}
