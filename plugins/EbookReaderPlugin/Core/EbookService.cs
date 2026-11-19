using EbookReaderPlugin.Parsing;

namespace EbookReaderPlugin.Core;

public sealed class ImportResult
{
    public List<BookRecord> Imported { get; } = new();

    public List<BookRecord> Skipped { get; } = new();

    public List<string> Errors { get; } = new();

    /// <summary>本次导入已解析的文档，用于导入后立即打开时避免重复解析。</summary>
    public Dictionary<string, EbookDocument> Documents { get; } = new(StringComparer.Ordinal);
}

/// <summary>电子书导入、解析与书架读写入口。</summary>
public sealed class EbookService
{
    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [".epub"] = "epub",
        [".mobi"] = "mobi",
        [".prc"] = "mobi",
        [".azw"] = "mobi",
        [".azw3"] = "azw3",
        [".fb2"] = "fb2",
        [".txt"] = "txt",
        [".text"] = "txt",
        [".md"] = "md",
        [".markdown"] = "md",
        [".html"] = "html",
        [".htm"] = "html",
        [".xhtml"] = "html",
    };

    public static readonly string[] SupportedExtensions =
        FormatNames.Keys.OrderBy(extension => extension).ToArray();

    public EbookService(string? libraryRoot = null)
    {
        Library = new BookLibrary(libraryRoot);
    }

    public BookLibrary Library { get; }

    public static bool IsSupported(string path) =>
        FormatNames.ContainsKey(Path.GetExtension(path));

    public static string GetFormat(string path) =>
        FormatNames.TryGetValue(Path.GetExtension(path), out var format) ? format : "";

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        foreach (var path in paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(path))
                {
                    result.Errors.Add($"{Path.GetFileName(path)}: 文件不存在");
                    continue;
                }

                if (!IsSupported(path))
                {
                    result.Errors.Add($"{Path.GetFileName(path)}: 不支持的格式");
                    continue;
                }

                var existing = Library.FindByOriginalPath(path);
                if (existing is not null)
                {
                    if (File.Exists(existing.StoredPath))
                    {
                        existing.LastOpenedAt = DateTime.UtcNow;
                        Library.Upsert(existing, save: false);
                        result.Skipped.Add(existing);
                        continue;
                    }

                    // 书籍文件已被手动删除，移除失效记录后重新导入
                    Library.Delete(existing);
                }

                var document = await Task.Run(() => EbookParser.Parse(path), cancellationToken);
                var id = Guid.NewGuid().ToString("N");
                var extension = Path.GetExtension(path);
                var storedPath = await Task.Run(
                    () => Library.CopyIntoStore(path, id, extension),
                    cancellationToken);
                var cover = await Task.Run(
                    () => Library.SaveCover(document.CoverImage, id),
                    cancellationToken);

                var book = new BookRecord
                {
                    Id = id,
                    Title = string.IsNullOrWhiteSpace(document.Title)
                        ? Path.GetFileNameWithoutExtension(path)
                        : document.Title,
                    Author = document.Author,
                    Format = GetFormat(path),
                    StoredPath = storedPath,
                    CoverPath = cover.Path,
                    CoverColor = cover.ColorHex,
                    CoverThumbnail = !string.IsNullOrWhiteSpace(cover.Path),
                    OriginalPath = Path.GetFullPath(path),
                    Position = 0,
                    TotalLength = document.TotalLength,
                    AddedAt = DateTime.UtcNow,
                    LastOpenedAt = DateTime.UtcNow,
                };

                Library.Upsert(book, save: false);
                result.Imported.Add(book);
                result.Documents[book.Id] = document;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (result.Imported.Count != 1)
            result.Documents.Clear();

        Library.Save();
        return result;
    }

    public async Task<EbookDocument> OpenAsync(
        BookRecord book,
        CancellationToken cancellationToken = default)
    {
        EnsureStoredFileExists(book);
        var document = await Task.Run(() => EbookParser.Parse(book.StoredPath), cancellationToken);
        return await CompleteOpenAsync(book, document, cancellationToken);
    }

    /// <summary>使用导入阶段已解析的文档打开，避免再次解析整本书。</summary>
    public async Task<EbookDocument> OpenAsync(
        BookRecord book,
        EbookDocument document,
        CancellationToken cancellationToken = default)
    {
        EnsureStoredFileExists(book);
        return await CompleteOpenAsync(book, document, cancellationToken);
    }

    private async Task<EbookDocument> CompleteOpenAsync(
        BookRecord book,
        EbookDocument document,
        CancellationToken cancellationToken)
    {
        if ((string.IsNullOrWhiteSpace(book.CoverPath) ||
             string.IsNullOrWhiteSpace(book.CoverColor)) &&
            document.CoverImage is { Length: > 0 })
        {
            var cover = await Task.Run(
                () => Library.SaveCover(document.CoverImage, book.Id),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(cover.Path))
            {
                book.CoverPath = cover.Path;
                book.CoverThumbnail = true;
            }
            if (!string.IsNullOrWhiteSpace(cover.ColorHex))
                book.CoverColor = cover.ColorHex;
        }

        document.CoverImage = null;

        book.TotalLength = document.TotalLength;
        if (string.IsNullOrWhiteSpace(book.Author) && !string.IsNullOrWhiteSpace(document.Author))
            book.Author = document.Author;
        if (string.IsNullOrWhiteSpace(book.Title) && !string.IsNullOrWhiteSpace(document.Title))
            book.Title = document.Title;
        book.LastOpenedAt = DateTime.UtcNow;
        Library.Upsert(book);

        return document;
    }

    private static void EnsureStoredFileExists(BookRecord book)
    {
        if (!File.Exists(book.StoredPath))
            throw new FileNotFoundException("书籍文件不存在，可能已被删除", book.StoredPath);
    }

    public void Delete(BookRecord book) => Library.Delete(book);

    public void SaveProgress(BookRecord book, long position, long totalLength) =>
        Library.UpdateProgress(book, position, totalLength);

    public void Flush() => Library.Flush();
}
