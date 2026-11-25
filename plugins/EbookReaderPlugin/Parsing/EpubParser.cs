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

        var images = LoadImages(book);
        var navigationPath = book.Content?.NavigationHtmlFile?.FilePath;
        var chapters = new List<Chapter>();
        var currentParentTitle = "";

        foreach (var file in book.ReadingOrder ?? new List<EpubLocalTextContentFile>())
        {
            if (!string.IsNullOrWhiteSpace(navigationPath) &&
                string.Equals(file.FilePath, navigationPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var storyTitle = HtmlTextConverter.ExtractStoryTitle(file.Content);
            if (!string.IsNullOrWhiteSpace(storyTitle))
                currentParentTitle = storyTitle;

            var navTitle = navTitles.TryGetValue(file.FilePath ?? "", out var mappedTitle)
                ? mappedTitle
                : "";
            var hasSectionHeading = HtmlTextConverter.HasSectionHeading(file.Content);
            var preferredTitle = hasSectionHeading && !string.IsNullOrWhiteSpace(currentParentTitle)
                ? currentParentTitle
                : !string.IsNullOrWhiteSpace(navTitle)
                    ? navTitle
                    : hasSectionHeading
                        ? currentParentTitle
                        : "";
            var fallbackTitle = string.IsNullOrWhiteSpace(preferredTitle)
                ? $"第 {chapters.Count + 1} 章"
                : preferredTitle;

            var (preparedHtml, fileImages) = HtmlTextConverter.ExtractImages(
                file.Content,
                source => ResolveImage(file.FilePath, source, images));
            var (_, fileChapters) = HtmlTextConverter.BuildFromHtml(
                preparedHtml,
                fallbackTitle,
                string.IsNullOrWhiteSpace(preferredTitle) ? null : preferredTitle,
                fileImages);

            chapters.AddRange(fileChapters);
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

    private static Dictionary<string, BookImage> LoadImages(EpubBook book)
    {
        var images = new Dictionary<string, BookImage>(StringComparer.OrdinalIgnoreCase);
        var localImages = book.Content?.Images?.Local;
        if (localImages is null)
            return images;

        foreach (var image in localImages)
        {
            if (image.Content is not { Length: > 0 } || string.IsNullOrWhiteSpace(image.FilePath))
                continue;

            images[NormalizeArchivePath(image.FilePath)] = new BookImage
            {
                Key = image.FilePath,
                Data = image.Content,
            };
        }

        return images;
    }

    private static BookImage? ResolveImage(
        string? htmlPath,
        string source,
        IReadOnlyDictionary<string, BookImage> images)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var value = source.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = value.IndexOf('#');
        if (hash >= 0)
            value = value[..hash];
        var query = value.IndexOf('?');
        if (query >= 0)
            value = value[..query];

        try
        {
            value = Uri.UnescapeDataString(value);
        }
        catch
        {
        }

        var path = ResolveArchivePath(htmlPath, value);
        return path.Length > 0 && images.TryGetValue(path, out var image) ? image : null;
    }

    private static string ResolveArchivePath(string? baseFile, string source)
    {
        source = source.Replace('\\', '/');
        var rooted = source.StartsWith('/');
        source = source.TrimStart('/');

        var baseDirectory = "";
        if (!rooted && !string.IsNullOrWhiteSpace(baseFile))
        {
            var normalizedBase = NormalizeArchivePath(baseFile);
            var slash = normalizedBase.LastIndexOf('/');
            if (slash >= 0)
                baseDirectory = normalizedBase[..(slash + 1)];
        }

        var parts = new List<string>();
        foreach (var part in (baseDirectory + source).Split('/'))
        {
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

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

}
