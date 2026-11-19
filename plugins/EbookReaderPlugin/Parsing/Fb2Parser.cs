using System.Text;
using System.Xml.Linq;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Parsing;

/// <summary>FictionBook 2 (.fb2) 解析器。</summary>
public static class Fb2Parser
{
    public static EbookDocument Parse(string path)
    {
        using var stream = File.OpenRead(path);
        var xml = XDocument.Load(stream);
        var root = xml.Root ?? throw new InvalidDataException("FB2 文件内容为空");
        var ns = root.Name.Namespace;

        var title = TextUtil.CleanInline(root.Descendants(ns + "book-title").FirstOrDefault()?.Value);
        if (string.IsNullOrWhiteSpace(title))
            title = TextUtil.FileTitle(path);

        var authorElement = root.Descendants(ns + "author").FirstOrDefault();
        var author = "";
        if (authorElement is not null)
        {
            var parts = new[] { "first-name", "middle-name", "last-name" }
                .Select(name => authorElement.Element(ns + name)?.Value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));
            author = TextUtil.CleanInline(string.Join(' ', parts));

            if (author.Length == 0)
                author = TextUtil.CleanInline(authorElement.Element(ns + "nickname")?.Value);
        }

        var chapters = new List<Chapter>();
        var body = root.Elements(ns + "body").FirstOrDefault();
        if (body is not null)
        {
            var sections = body.Elements(ns + "section").ToList();
            if (sections.Count > 0)
            {
                foreach (var section in sections)
                    CollectSection(section, ns, chapters, 1);
            }
            else
            {
                var text = CollectBlocks(body, ns);
                if (text.Length > 0)
                    chapters.Add(new Chapter { Title = title, Text = text });
            }
        }

        if (chapters.Count == 0)
            throw new InvalidDataException("FB2 中没有可阅读的文本章节");

        var document = new EbookDocument
        {
            Title = title,
            Author = author,
            CoverImage = ExtractCover(root, ns),
        };
        document.Chapters.AddRange(chapters);
        return document;
    }

    private static byte[]? ExtractCover(XElement root, XNamespace ns)
    {
        var xlink = XNamespace.Get("http://www.w3.org/1999/xlink");
        var href = root.Descendants(ns + "coverpage")
            .Elements(ns + "image")
            .FirstOrDefault()
            ?.Attribute(xlink + "href")
            ?.Value;

        if (string.IsNullOrWhiteSpace(href))
            return null;

        var id = href.TrimStart('#');
        var binary = root.Descendants(ns + "binary")
            .FirstOrDefault(element => element.Attribute("id")?.Value == id);

        if (binary is null)
            return null;

        try
        {
            var base64 = new string(binary.Value.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            return Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectSection(XElement section, XNamespace ns, List<Chapter> chapters, int depth)
    {
        var title = ExtractTitle(section, ns);
        var text = CollectBlocks(section, ns);

        if (text.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(title))
                title = $"第 {chapters.Count + 1} 节";

            chapters.Add(new Chapter { Title = title, Text = text });
        }

        foreach (var child in section.Elements(ns + "section"))
            CollectSection(child, ns, chapters, depth + 1);
    }

    private static string ExtractTitle(XElement section, XNamespace ns)
    {
        var titleElement = section.Element(ns + "title");
        if (titleElement is null)
            return "";

        var builder = new StringBuilder();
        foreach (var node in titleElement.DescendantNodes().OfType<XText>())
        {
            var value = TextUtil.CleanInline(node.Value);
            if (value.Length > 0)
                builder.Append(value).Append(' ');
        }

        var title = TextUtil.CleanInline(builder.ToString());
        return title.Length > 80 ? title[..80] : title;
    }

    private static string CollectBlocks(XElement parent, XNamespace ns)
    {
        var builder = new StringBuilder();

        foreach (var child in parent.Elements())
        {
            var name = child.Name.LocalName;
            if (name == "section" || name == "title")
                continue;

            switch (name)
            {
                case "empty-line":
                    builder.Append('\n');
                    break;

                case "p":
                case "v":
                case "subtitle":
                case "text-author":
                case "date":
                    AppendParagraph(builder, child.Value);
                    break;

                case "cite":
                case "epigraph":
                case "poem":
                case "stanza":
                case "annotation":
                    foreach (var paragraph in child.Descendants()
                                 .Where(element => element.Name.LocalName is "p" or "v" or "subtitle" or "text-author"))
                        AppendParagraph(builder, paragraph.Value);
                    break;

                case "table":
                    foreach (var row in child.Descendants(ns + "tr"))
                    {
                        var cells = row.Elements(ns + "td")
                            .Select(cell => TextUtil.CleanInline(cell.Value))
                            .Where(value => value.Length > 0);
                        AppendParagraph(builder, string.Join(" | ", cells));
                    }
                    break;
            }
        }

        return TextUtil.Normalize(builder.ToString());
    }

    private static void AppendParagraph(StringBuilder builder, string? value)
    {
        var text = TextUtil.CleanInline(value);
        if (text.Length == 0)
            return;

        builder.Append(text).Append("\n\n");
    }
}
