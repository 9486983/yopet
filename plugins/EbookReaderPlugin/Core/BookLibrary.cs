using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EbookReaderPlugin.Core;

public readonly record struct CoverSaveResult(string Path, string ColorHex)
{
    public static CoverSaveResult Empty => new("", "");
}

/// <summary>电子书书架存储：元数据 JSON + 导入后复制到 AppData 的书籍文件。</summary>
public sealed class BookLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly object _writeGate = new();
    private readonly List<BookRecord> _books = new();
    private bool _saveRequested;
    private bool _saveWorkerRunning;

    public BookLibrary(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "yopet",
                "ebook-reader")
            : rootDirectory;
        BooksDirectory = Path.Combine(RootDirectory, "books");
        CoversDirectory = Path.Combine(RootDirectory, "covers");
        LibraryFile = Path.Combine(RootDirectory, "library.json");

        Directory.CreateDirectory(BooksDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Load();
    }

    public string RootDirectory { get; }

    public string BooksDirectory { get; }

    public string CoversDirectory { get; }

    public string LibraryFile { get; }

    public IReadOnlyList<BookRecord> GetBooks()
    {
        lock (_gate)
        {
            return _books
                .OrderByDescending(book => book.LastOpenedAt)
                .ThenByDescending(book => book.AddedAt)
                .ToList();
        }
    }

    public BookRecord? FindById(string id)
    {
        lock (_gate)
        {
            return _books.FirstOrDefault(book => book.Id == id);
        }
    }

    public BookRecord? FindByOriginalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_gate)
        {
            return _books.FirstOrDefault(book =>
                string.Equals(book.OriginalPath, fullPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Upsert(BookRecord book, bool save = true)
    {
        lock (_gate)
        {
            var index = _books.FindIndex(item => item.Id == book.Id);
            if (index >= 0)
                _books[index] = book;
            else
                _books.Add(book);
        }

        if (save)
            Save();
    }

    public void Delete(BookRecord book)
    {
        lock (_gate)
        {
            _books.RemoveAll(item => item.Id == book.Id);
        }

        Save();
        TryDeleteStoredFile(book.StoredPath);
        TryDeleteStoredFile(book.CoverPath);
    }

    /// <summary>在书架锁内修改书籍及其书签/批注，避免后台快照序列化时读到半更新状态。</summary>
    public void Mutate(BookRecord book, Action<BookRecord> mutation)
    {
        lock (_gate)
        {
            mutation(book);
        }
    }

    /// <summary>只在内存中更新阅读进度；由面板在返回书架/关闭时统一落盘。</summary>
    public void UpdateProgress(BookRecord book, long position, long totalLength)
    {
        lock (_gate)
        {
            book.Position = Math.Max(0, position);
            book.TotalLength = Math.Max(totalLength, 0);
            book.LastOpenedAt = DateTime.UtcNow;
        }
    }

    public string CopyIntoStore(string sourcePath, string id, string extension)
    {
        Directory.CreateDirectory(BooksDirectory);
        var target = Path.Combine(BooksDirectory, id + extension.ToLowerInvariant());

        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase))
            return target;

        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    /// <summary>把封面缩放成缩略图并提取主色；失败时返回空结果。</summary>
    public CoverSaveResult SaveCover(byte[]? data, string id)
    {
        if (data is null || data.Length == 0)
            return CoverSaveResult.Empty;

        var thumbnail = CoverImageProcessor.Create(data);
        if (thumbnail is null)
            return CoverSaveResult.Empty;

        Directory.CreateDirectory(CoversDirectory);
        var target = Path.Combine(CoversDirectory, id + ".png");
        File.WriteAllBytes(target, thumbnail.Png);
        return new CoverSaveResult(target, thumbnail.ColorHex);
    }

    /// <summary>把书架标记为待保存，由后台线程完成序列化与写文件。</summary>
    public void Save()
    {
        lock (_saveGate)
        {
            _saveRequested = true;
            if (_saveWorkerRunning)
                return;

            _saveWorkerRunning = true;
            _ = Task.Run(WritePendingLoop);
        }
    }

    /// <summary>同步落盘最新快照；仅在关闭/退出等需要确定性的场景调用。</summary>
    public void Flush()
    {
        lock (_saveGate)
        {
            _saveRequested = false;
        }

        lock (_writeGate)
        {
            WriteJsonUnsafe(CreateSnapshot());
        }
    }

    private string CreateSnapshot()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new LibraryData { Books = _books }, JsonOptions);
        }
    }

    private void WritePendingLoop()
    {
        try
        {
            while (true)
            {
                lock (_saveGate)
                {
                    if (!_saveRequested)
                    {
                        _saveWorkerRunning = false;
                        return;
                    }

                    _saveRequested = false;
                }

                lock (_writeGate)
                {
                    WriteJsonUnsafe(CreateSnapshot());
                }
            }
        }
        catch
        {
            lock (_saveGate)
            {
                _saveWorkerRunning = false;
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(LibraryFile))
                return;

            var json = File.ReadAllText(LibraryFile, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<LibraryData>(json, JsonOptions);
            if (data?.Books is null)
                return;

            _books.AddRange(data.Books
                .Where(book => !string.IsNullOrWhiteSpace(book.Id))
                .GroupBy(book => book.Id)
                .Select(group => group.First()));
        }
        catch
        {
            // 书架文件损坏时从空书架开始，不阻塞插件启动
        }
    }

    private void WriteJsonUnsafe(string json)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var temporary = LibraryFile + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, LibraryFile, overwrite: true);
        }
        catch
        {
            // 磁盘暂时不可写时保留内存中的书架，下次保存再试
        }
    }

    private void TryDeleteStoredFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var fullPath = Path.GetFullPath(path);
            var booksRoot = Path.GetFullPath(BooksDirectory) + Path.DirectorySeparatorChar;
            var coversRoot = Path.GetFullPath(CoversDirectory) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(booksRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(coversRoot, StringComparison.OrdinalIgnoreCase))
                File.Delete(fullPath);
        }
        catch
        {
            // 删除失败不影响书架记录
        }
    }
}
