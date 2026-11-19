using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>按扩展名选择电子书解析器。</summary>
public static class EbookParser
{
    public static EbookDocument Parse(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".epub" => EpubParser.Parse(path),
            ".fb2" => Fb2Parser.Parse(path),
            ".mobi" or ".prc" or ".azw" or ".azw3" or ".azw4" => MobiParser.Parse(path),
            ".html" or ".htm" or ".xhtml" => ParseHtml(path),
            ".txt" or ".text" or ".md" or ".markdown" => PlainTextParser.Parse(path),
            _ => throw new NotSupportedException($"不支持的电子书格式：{extension}"),
        };
    }

    private static EbookDocument ParseHtml(string path)
    {
        var html = PlainTextParser.ReadTextFile(path);
        var (title, chapters) = HtmlTextConverter.BuildFromHtml(html, TextUtil.FileTitle(path));
        if (chapters.Count == 0)
            throw new InvalidDataException("HTML 文件中没有可阅读的文本");

        var document = new EbookDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? TextUtil.FileTitle(path) : title,
        };
        document.Chapters.AddRange(chapters);
        return document;
    }
}
