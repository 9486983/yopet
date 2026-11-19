using System.Text;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>TXT / Markdown 等纯文本电子书。</summary>
public static class PlainTextParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    static PlainTextParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static EbookDocument Parse(string path)
    {
        var text = ReadTextFile(path);
        var title = TextUtil.FileTitle(path);
        var author = TextUtil.TryGetAuthor(text);
        var chapters = TextUtil.SplitChapters(text, title);

        if (chapters.Count == 0)
            throw new InvalidDataException("文件中没有可阅读的文本");

        var document = new EbookDocument
        {
            Title = title,
            Author = author,
        };
        document.Chapters.AddRange(chapters);
        return document;
    }

    public static string ReadTextFile(string path) => Decode(File.ReadAllBytes(path));

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "";

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
        }

        try
        {
            var gb18030 = Encoding.GetEncoding(
                "GB18030",
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return gb18030.GetString(bytes);
        }
        catch
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
