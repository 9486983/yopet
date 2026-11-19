using Avalonia.Media;
using Avalonia.Media.Imaging;
using EbookReaderPlugin.Core;

namespace EbookReaderPlugin.Views;

/// <summary>书架展示项：书籍记录 + 缩略封面 + 封面主色。</summary>
public sealed class BookShelfItem : IDisposable
{
    public BookShelfItem(BookRecord book, Bitmap? cover, Color? accentColor)
    {
        Book = book;
        Cover = cover;
        AccentColor = accentColor;
    }

    public BookRecord Book { get; }

    public Bitmap? Cover { get; }

    public Color? AccentColor { get; }

    public string Title => Book.Title;

    public string Author => Book.Author;

    public string FormatLabel => Book.FormatLabel;

    public double ProgressPercent => Book.ProgressPercent;

    public string ProgressText => Book.ProgressText;

    public void Dispose() => Cover?.Dispose();
}
