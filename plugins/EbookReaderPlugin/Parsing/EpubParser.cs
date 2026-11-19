using EbookReaderPlugin.Core;
using VersOne.Epub;
using VersOne.Epub.Options;

namespace EbookReaderPlugin.Parsing;

/// <summary>EPUB 2 / EPUB 3 解析器（使用 VersOne.Epub 处理容器、OPF、NCX/Nav）。</summary>
public static class EpubParser
{
    public static EbookDocument Parse(string path)
    {
        var book = EpubReader.ReadBook(path, CreateTolerantOptions())
            ?? throw new InvalidDataException("EPUB 内容为空");
        var title = TextUtil.CleanInline(book.Title);
        if (string.IsNullOrWhiteSpace(title))
            title = TextUtil.FileTitle(path);

        var author = TextUtil.CleanInline(book.Author);
        if (string.IsNullOrWhiteSpace(author) && book.AuthorList is { Count: > 0 })
            author = TextUtil.CleanInline(string.Join(", ", book.AuthorList));

        var navTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectNavigationTitles(book.Navigation, navTitles);

        var navigationPath = book.Content?.NavigationHtmlFile?.FilePath;
        var chapters = new List<Chapter>();

        foreach (var file in book.ReadingOrder ?? new List<EpubLocalTextContentFile>())
        {
            if (!string.IsNullOrWhiteSpace(navigationPath) &&
                string.Equals(file.FilePath, navigationPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var body = HtmlTextConverter.ToPlainText(file.Content);
            if (body.Length == 0)
                continue;

            var heading = ExtractHeading(file.Content);
            var chapterTitle = navTitles.TryGetValue(file.FilePath ?? "", out var navTitle)
                ? navTitle
                : heading;

            if (string.IsNullOrWhiteSpace(chapterTitle))
                chapterTitle = $"第 {chapters.Count + 1} 章";

            if (!string.IsNullOrWhiteSpace(heading) &&
                body.StartsWith(heading, StringComparison.Ordinal))
            {
                body = body[heading.Length..].TrimStart('\n', '\r', ' ', '\t');
            }

            if (body.Length == 0)
                continue;

            chapters.Add(new Chapter { Title = chapterTitle, Text = body });
        }

        if (chapters.Count == 0)
            throw new InvalidDataException("EPUB 中没有可阅读的文本章节");

        var document = new EbookDocument
        {
            Title = title,
            Author = author,
            CoverImage = book.CoverImage is { Length: > 0 } ? book.CoverImage : null,
        };
        document.Chapters.AddRange(chapters);
        return document;
    }

    private static EpubReaderOptions CreateTolerantOptions() => new()
    {
        Epub2NcxReaderOptions = new Epub2NcxReaderOptions
        {
            IgnoreMissingTocManifestItemError = true,
            IgnoreMissingTocFileError = true,
            IgnoreTocFileIsNotValidXmlError = true,
            IgnoreMissingNcxElementError = true,
            IgnoreMissingHeadElementError = true,
            IgnoreMissingDocTitleElementError = true,
            IgnoreMissingNavMapElementError = true,
            SkipInvalidNavigationContent = true,
            SkipInvalidNavigationLabels = true,
        },
        Epub3NavDocumentReaderOptions = new Epub3NavDocumentReaderOptions
        {
            IgnoreMissingNavManifestItemError = true,
            IgnoreMissingNavFileError = true,
            IgnoreNavFileIsNotValidXmlError = true,
            IgnoreMissingHtmlElementError = true,
            IgnoreMissingBodyElementError = true,
            SkipInvalidLiElements = true,
        },
        NavigationReaderOptions = new NavigationReaderOptions
        {
            AllowEpub2NavigationItemsWithEmptyTitles = true,
            SkipNavigationItemsReferencingMissingContent = true,
        },
        ContentReaderOptions = new ContentReaderOptions
        {
            IgnoreMissingFileError = true,
            IgnoreFileIsTooLargeError = true,
            SkipItemsWithDuplicateHrefs = true,
            SkipItemsWithDuplicateFilePaths = true,
        },
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
            SkipDuplicateManifestItemIds = true,
            SkipDuplicateManifestHrefs = true,
            SkipInvalidSpineItems = true,
        },
        SpineReaderOptions = new SpineReaderOptions
        {
            IgnoreMissingManifestItems = true,
            IgnoreMissingContentFiles = true,
        },
    };

    private static void CollectNavigationTitles(
        IEnumerable<EpubNavigationItem>? items,
        Dictionary<string, string> titles)
    {
        if (items is null)
            return;

        foreach (var item in items)
        {
            var path = item.HtmlContentFile?.FilePath;
            var title = TextUtil.CleanInline(item.Title);
            if (!string.IsNullOrWhiteSpace(path) &&
                !string.IsNullOrWhiteSpace(title))
            {
                titles.TryAdd(path, title);
            }

            CollectNavigationTitles(item.NestedItems, titles);
        }
    }

    private static string ExtractHeading(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            "<h[12][^>]*>(?<title>.*?)</h[12]>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!match.Success)
            return "";

        var title = HtmlTextConverter.ToPlainText(match.Groups["title"].Value);
        return title.Length > 80 ? title[..80] : title;
    }
}
