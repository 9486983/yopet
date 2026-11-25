using System.Text.Json.Serialization;

namespace EbookReaderPlugin.Core;

/// <summary>电子书中的一个章节。</summary>
public sealed class Chapter
{
    public string Title { get; set; } = "";

    public string Text { get; set; } = "";

    public List<BookImage> Images { get; } = new();
}

/// <summary>章节中的插图，正文用占位符标记其位置。</summary>
public sealed class BookImage
{
    public const char Placeholder = '\uFFFC';

    public string Key { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>解析后的电子书内容（与具体格式无关）。</summary>
public sealed class EbookDocument
{
    private long[]? _chapterStarts;
    private long _totalLength;

    public string Title { get; set; } = "";

    public string Author { get; set; } = "";

    public List<Chapter> Chapters { get; } = new();

    /// <summary>可选封面图（由解析器提取，导入时复制到书架目录）。</summary>
    public byte[]? CoverImage { get; set; }

    /// <summary>全书正文总字符数（用于阅读进度）。</summary>
    public long TotalLength
    {
        get
        {
            EnsureLayout();
            return _totalLength;
        }
    }

    /// <summary>指定章节正文在全书中的起始字符偏移。</summary>
    public long GetChapterStart(int chapterIndex)
    {
        EnsureLayout();
        if (chapterIndex <= 0 || _chapterStarts is null || _chapterStarts.Length == 0)
            return 0;

        return chapterIndex >= _chapterStarts.Length
            ? _totalLength
            : _chapterStarts[chapterIndex];
    }

    /// <summary>把全书字符偏移定位到「章节 + 章节内偏移」。</summary>
    public (int ChapterIndex, int Offset) Locate(long position)
    {
        EnsureLayout();
        if (_chapterStarts is null || _chapterStarts.Length == 0)
            return (0, 0);

        if (position <= 0)
            return (0, 0);

        var index = Array.BinarySearch(_chapterStarts, position);
        if (index < 0)
            index = ~index - 1;

        if (index < 0)
            return (0, 0);

        if (index >= Chapters.Count)
            index = Chapters.Count - 1;

        var start = _chapterStarts[index];
        var length = Chapters[index].Text.Length;
        var offset = (int)Math.Clamp(position - start, 0, length);
        return (index, offset);
    }

    private void EnsureLayout()
    {
        if (_chapterStarts is not null)
            return;

        _chapterStarts = new long[Chapters.Count];
        long start = 0;
        for (var index = 0; index < Chapters.Count; index++)
        {
            _chapterStarts[index] = start;
            start += Chapters[index].Text.Length;
        }

        _totalLength = start;
    }
}

/// <summary>书架中的一条书籍记录（持久化到 AppData）。</summary>
public sealed class BookRecord
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Author { get; set; } = "";

    public string Format { get; set; } = "";

    public string StoredPath { get; set; } = "";

    public string CoverPath { get; set; } = "";

    /// <summary>从封面提取的主色（#RRGGBB），用于书脊配色。</summary>
    public string CoverColor { get; set; } = "";

    /// <summary>CoverPath 是否已经是小尺寸缩略图。</summary>
    public bool CoverThumbnail { get; set; }

    public string OriginalPath { get; set; } = "";

    public long Position { get; set; }

    public long TotalLength { get; set; }

    public DateTime AddedAt { get; set; }

    public DateTime LastOpenedAt { get; set; }

    public List<BookBookmark> Bookmarks { get; set; } = new();

    public List<BookAnnotation> Annotations { get; set; } = new();

    [JsonIgnore]
    public string FormatLabel => string.IsNullOrWhiteSpace(Format) ? "BOOK" : Format.ToUpperInvariant();

    [JsonIgnore]
    public string MetaLine
    {
        get
        {
            var author = string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
            return $"{author} · {FormatLabel}";
        }
    }

    [JsonIgnore]
    public string ProgressText
    {
        get
        {
            var percent = ProgressPercent;
            return percent <= 0 ? "未读" : $"{percent:0}%";
        }
    }

    [JsonIgnore]
    public double ProgressPercent
    {
        get
        {
            if (TotalLength <= 0)
                return 0;

            return Math.Clamp(Position * 100.0 / TotalLength, 0, 100);
        }
    }
}

/// <summary>书架持久化文件结构。</summary>
public sealed class LibraryData
{
    public List<BookRecord> Books { get; set; } = new();
}

/// <summary>阅读书签（章节内字符偏移）。</summary>
public sealed class BookBookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int ChapterIndex { get; set; }

    public int Offset { get; set; }

    public string Label { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>正文高亮/批注（章节内字符区间）。</summary>
public sealed class BookAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int ChapterIndex { get; set; }

    public int Start { get; set; }

    public int End { get; set; }

    public string Note { get; set; } = "";

    public string Color { get; set; } = "#66FFD54F";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
