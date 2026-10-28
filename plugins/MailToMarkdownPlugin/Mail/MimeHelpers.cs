using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MailToMarkdownPlugin.Mail;

internal static class MimeHelpers
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex EncodedWord = new(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AdjacentEncodedWords = new(@"(\?=)\s+(?==\?)", RegexOptions.Compiled);
    private static readonly Regex ContinuationKey = new(@"^(.*?)\*(\d+)$", RegexOptions.Compiled);

    static MimeHelpers()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string DecodeHeaderValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var text = RepairRawUtf8(value);
        text = AdjacentEncodedWords.Replace(text, "$1");
        return EncodedWord.Replace(text, DecodeEncodedWord);
    }

    public static (string Value, Dictionary<string, string> Parameters) ParseParameters(string? headerValue)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(headerValue)) return ("", parameters);

        var segments = SplitSemicolon(headerValue);
        var value = segments.Count > 0 ? segments[0].Trim() : "";
        var continuations = new Dictionary<string, SortedDictionary<int, (string Raw, bool Extended)>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < segments.Count; i++)
        {
            var segment = segments[i].Trim();
            if (segment.Length == 0) continue;

            var equals = segment.IndexOf('=');
            if (equals <= 0) continue;

            var key = segment[..equals].Trim();
            var raw = Unquote(segment[(equals + 1)..].Trim());
            var extended = key.EndsWith('*');
            if (extended) key = key[..^1];

            var match = ContinuationKey.Match(key);
            if (match.Success)
            {
                var baseKey = match.Groups[1].Value;
                if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    continue;
                if (!continuations.TryGetValue(baseKey, out var parts))
                {
                    parts = new SortedDictionary<int, (string, bool)>();
                    continuations[baseKey] = parts;
                }
                parts[index] = (raw, extended);
                continue;
            }

            parameters[key] = extended ? DecodeRfc2231(raw) : raw;
        }

        foreach (var (key, parts) in continuations)
        {
            if (parameters.ContainsKey(key)) continue;

            var builder = new StringBuilder();
            string? charset = null;
            foreach (var (_, part) in parts)
            {
                if (!part.Extended)
                {
                    builder.Append(part.Raw);
                    continue;
                }

                if (charset == null)
                {
                    var (partCharset, decoded) = DecodeRfc2231Part(part.Raw);
                    charset = partCharset;
                    builder.Append(decoded);
                }
                else
                {
                    builder.Append(DecodeBytes(PercentDecode(part.Raw), charset));
                }
            }
            parameters[key] = builder.ToString();
        }

        return (value, parameters);
    }

    public static string DecodeRfc2231(string value)
    {
        var (_, decoded) = DecodeRfc2231Part(value);
        return decoded;
    }

    private static (string? Charset, string Decoded) DecodeRfc2231Part(string value)
    {
        var first = value.IndexOf('\'');
        if (first > 0)
        {
            var second = value.IndexOf('\'', first + 1);
            if (second > first)
            {
                var charset = value[..first];
                var encoded = value[(second + 1)..];
                return (charset, DecodeBytes(PercentDecode(encoded), charset));
            }
        }

        return (null, DecodeBytes(PercentDecode(value), null));
    }

    public static string DecodeBytes(byte[] bytes, string? charset)
    {
        if (bytes.Length == 0) return "";

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        var encoding = TryGetEncoding(charset);
        if (encoding != null)
        {
            if (IsAsciiLike(charset) && HasNonAscii(bytes))
            {
                if (TryDecode(StrictUtf8, bytes, out var utf8Text)) return utf8Text;
            }

            try
            {
                return encoding.GetString(bytes);
            }
            catch
            {
            }
        }

        if (TryDecode(StrictUtf8, bytes, out var decoded))
            return decoded;

        var gb = TryGetEncoding("gb18030");
        if (gb != null)
        {
            try
            {
                return gb.GetString(bytes);
            }
            catch
            {
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }

    public static byte[] DecodeBase64(string value)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<byte>();

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/')
                builder.Append(c);
            else if (c == '=')
                break;
        }

        var clean = builder.ToString();
        if (clean.Length == 0) return Array.Empty<byte>();

        var remainder = clean.Length % 4;
        if (remainder != 0) clean += new string('=', 4 - remainder);

        try
        {
            return Convert.FromBase64String(clean);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    public static byte[] DecodeQuotedPrintable(byte[] data)
    {
        if (data.Length == 0) return data;

        var output = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (b != (byte)'=')
            {
                output.Add(b);
                continue;
            }

            if (i + 1 < data.Length && (data[i + 1] == (byte)'\r' || data[i + 1] == (byte)'\n'))
            {
                if (data[i + 1] == (byte)'\r' && i + 2 < data.Length && data[i + 2] == (byte)'\n')
                    i += 2;
                else
                    i += 1;
                continue;
            }

            if (i + 2 < data.Length && IsHex(data[i + 1]) && IsHex(data[i + 2]))
            {
                output.Add((byte)((HexValue(data[i + 1]) << 4) | HexValue(data[i + 2])));
                i += 2;
                continue;
            }

            output.Add(b);
        }

        return output.ToArray();
    }


    public static (string Extension, string MimeType)? SniffMediaType(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return (".png", "image/png");
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return (".jpg", "image/jpeg");
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            return (".gif", "image/gif");
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return (".bmp", "image/bmp");
        if (data.Length >= 12 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return (".webp", "image/webp");
        if (data.Length >= 4 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F')
            return (".pdf", "application/pdf");
        if (data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'K' && data[2] == 3 && data[3] == 4)
            return (".zip", "application/zip");
        if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01 && data[3] == 0x00)
            return (".ico", "image/x-icon");

        return null;
    }

    public static void ApplySniffedType(MailAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        var hasExtension = !string.IsNullOrWhiteSpace(extension);
        var isOctetStream = string.Equals(attachment.MimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);
        if (hasExtension && !isOctetStream) return;

        var sniffed = SniffMediaType(attachment.Data);
        if (sniffed == null) return;

        if (!hasExtension)
        {
            attachment.FileName += sniffed.Value.Extension;
        }
        else if (isOctetStream && extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            attachment.FileName = Path.GetFileNameWithoutExtension(attachment.FileName) + sniffed.Value.Extension;
        }

        if (isOctetStream) attachment.MimeType = sniffed.Value.MimeType;
    }

    public static string ExtensionForMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "application/pdf" => ".pdf",
        "message/rfc822" => ".eml",
        "text/calendar" => ".ics",
        "application/ics" => ".ics",
        "application/zip" => ".zip",
        "text/html" => ".html",
        "text/plain" => ".txt",
        _ => ".bin",
    };

    private static string DecodeEncodedWord(Match match)
    {
        var charset = match.Groups[1].Value.Split('*')[0];
        var mode = match.Groups[2].Value.ToUpperInvariant();
        var payload = match.Groups[3].Value;

        var bytes = mode == "B"
            ? DecodeBase64(payload)
            : DecodeQuotedPrintable(Encoding.Latin1.GetBytes(payload.Replace('_', ' ')));

        return DecodeBytes(bytes, charset);
    }

    private static string RepairRawUtf8(string value)
    {
        if (!HasNonAscii(Encoding.Latin1.GetBytes(value))) return value;

        var bytes = Encoding.Latin1.GetBytes(value);
        return TryDecode(StrictUtf8, bytes, out var decoded) ? decoded : value;
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return null;

        var name = charset.Trim().Trim('"', '\'').Split('*')[0].Trim();
        if (name.Length == 0) return null;

        if (name.Equals("utf8", StringComparison.OrdinalIgnoreCase)) name = "utf-8";
        if (name.Equals("gb2312", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("gbk", StringComparison.OrdinalIgnoreCase))
            name = "gb18030";
        if (name.Equals("unknown-8bit", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("x-unknown", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAsciiLike(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return false;

        var name = charset.Trim().Trim('"', '\'').Split('*')[0].Trim();
        return name.Equals("us-ascii", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ascii", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("latin1", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("windows-1252", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNonAscii(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b > 0x7F) return true;
        }
        return false;
    }

    private static bool TryDecode(Encoding encoding, byte[] bytes, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch
        {
            text = "";
            return false;
        }
    }

    private static List<string> SplitSemicolon(string value)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        var quote = '\0';

        foreach (var c in value)
        {
            if (quoted)
            {
                builder.Append(c);
                if (c == quote) quoted = false;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                quoted = true;
                quote = c;
                builder.Append(c);
                continue;
            }

            if (c == ';')
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        result.Add(builder.ToString());
        return result;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2) return value;
        if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            return value[1..^1];
        return value;
    }

    private static byte[] PercentDecode(string value)
    {
        var output = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && IsHex((byte)value[i + 1]) && IsHex((byte)value[i + 2]))
            {
                output.Add((byte)((HexValue((byte)value[i + 1]) << 4) | HexValue((byte)value[i + 2])));
                i += 2;
                continue;
            }

            var c = value[i];
            if (c <= 0x7F)
                output.Add((byte)c);
            else
                output.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
        }
        return output.ToArray();
    }

    private static bool IsHex(byte value) =>
        (value >= (byte)'0' && value <= (byte)'9') ||
        (value >= (byte)'A' && value <= (byte)'F') ||
        (value >= (byte)'a' && value <= (byte)'f');

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => value - 'a' + 10,
    };
}
