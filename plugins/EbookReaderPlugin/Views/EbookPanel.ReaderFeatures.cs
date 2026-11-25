using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EbookReaderPlugin.Core;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace EbookReaderPlugin.Views;

public partial class EbookPanel
{    private void SetPageInlines(PageTextSlice page)
    {
        var inlines = PageText.Inlines;
        if (inlines is null)
            return;

        inlines.Clear();
        PageText.SelectionStart = 0;
        PageText.SelectionEnd = 0;
        var text = page.Text.Replace(BookImage.Placeholder, '\u200B');
        if (text.Length == 0)
            return;

        if (_chapterAnnotations.Count > 0)
        {
            var cursor = 0;
            foreach (var annotation in _chapterAnnotations)
            {
                var start = Math.Clamp(annotation.Start - page.CharStart, 0, text.Length);
                var end = Math.Clamp(annotation.End - page.CharStart, 0, text.Length);
                if (end <= start || end <= cursor)
                    continue;

                start = Math.Max(start, cursor);
                if (start > cursor)
                    inlines.Add(new Run(text[cursor..start]));

                inlines.Add(new Run(text[start..end])
                {
                    Background = ParseAnnotationColor(annotation.Color),
                });
                cursor = end;
            }

            if (cursor < text.Length)
                inlines.Add(new Run(text[cursor..]));
            return;
        }

        inlines.Add(new Run(text));
    }

    private IBrush ParseAnnotationColor(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "#66FFD54F" : value.Trim();
        if (_annotationBrushCache.TryGetValue(key, out var cached))
            return cached;

        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(key));
        }
        catch
        {
            brush = new SolidColorBrush(Color.Parse("#66FFD54F"));
        }

        _annotationBrushCache[key] = brush;
        return brush;
    }

    private void RefreshChapterAnnotations()
    {
        _chapterAnnotations.Clear();
        if (_book?.Annotations is not { Count: > 0 } annotations)
            return;

        _chapterAnnotations.AddRange(annotations
            .Where(annotation => annotation.ChapterIndex == _chapterIndex)
            .OrderBy(annotation => annotation.Start));
    }

    private void OnPageTextPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible)
            return;

        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        Dispatcher.UIThread.Post(HandlePageTextGesture, DispatcherPriority.Input);
    }

    private void HandlePageTextGesture()
    {
        if (_document is null || !ReaderPanel.IsVisible || ReaderListOverlay.IsVisible)
            return;

        var start = Math.Min(PageText.SelectionStart, PageText.SelectionEnd);
        var end = Math.Max(PageText.SelectionStart, PageText.SelectionEnd);

        if (end > start)
        {
            ShowSelectionToolbar(start, end);
            return;
        }

        if (SelectionToolbar.IsVisible)
            HideSelectionToolbar();
    }

    private void ShowSelectionToolbar(int start, int end)
    {
        _selectionStart = Math.Max(0, start);
        _selectionEnd = Math.Max(_selectionStart, end);
        SetReaderChrome(false);
        SelectionToolbar.IsVisible = true;
    }

    private void HideSelectionToolbar()
    {
        SelectionToolbar.IsVisible = false;
        _selectionStart = 0;
        _selectionEnd = 0;
        PageText.SelectionStart = 0;
        PageText.SelectionEnd = 0;
    }

    private void OnSelectionToolbarPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnHighlightSelectionClick(object? sender, RoutedEventArgs e) =>
        AddSelectionAnnotation("", "#66FFD54F");

    private async void OnAnnotateSelectionClick(object? sender, RoutedEventArgs e)
    {
        _suppressDeactivateClose = true;
        try
        {
            var note = await _host.ShowInputDialog(
                L("AnnotationTitle", "添加批注"),
                L("AnnotationPlaceholder", "输入批注内容。"));

            if (string.IsNullOrWhiteSpace(note))
                return;

            AddSelectionAnnotation(note.Trim(), "#66FFB74D");
        }
        catch (Exception ex)
        {
            _host.ShowThought(L("AnnotationTitle", "添加批注"), ex.Message);
        }
        finally
        {
            _suppressDeactivateClose = false;
        }
    }

    private void AddSelectionAnnotation(string note, string color)
    {
        if (_book is null || _pages.Count == 0)
            return;

        var page = _pages[_pageIndex];
        var pageEnd = page.CharStart + page.Text.Length;
        var start = Math.Clamp(page.CharStart + _selectionStart, page.CharStart, pageEnd);
        var end = Math.Clamp(page.CharStart + _selectionEnd, start, pageEnd);
        if (end <= start)
            return;

        _service.Library.Mutate(_book, book =>
        {
            book.Annotations ??= new List<BookAnnotation>();
            book.Annotations.Add(new BookAnnotation
            {
                ChapterIndex = _chapterIndex,
                Start = start,
                End = end,
                Note = note,
                Color = color,
            });
        });
        RefreshChapterAnnotations();
        _service.Library.Save();
        HideSelectionToolbar();
        RenderPage();
    }

    private void OnCancelSelectionClick(object? sender, RoutedEventArgs e) => HideSelectionToolbar();

    private void OnBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (_book is null || _pages.Count == 0)
            return;

        var page = _pages[_pageIndex];
        _service.Library.Mutate(_book, book =>
        {
            book.Bookmarks ??= new List<BookBookmark>();
            var existing = book.Bookmarks.FirstOrDefault(bookmark =>
                bookmark.ChapterIndex == _chapterIndex && bookmark.Offset == page.PositionOffset);

            if (existing is not null)
            {
                book.Bookmarks.Remove(existing);
            }
            else
            {
                book.Bookmarks.Add(new BookBookmark
                {
                    ChapterIndex = _chapterIndex,
                    Offset = page.PositionOffset,
                    Label = _document?.Chapters[_chapterIndex].Title ?? "",
                });
            }
        });

        _service.Library.Save();
        UpdateBookmarkButtonState();
    }

    private void UpdateBookmarkButtonState()
    {
        if (_book is null || _pages.Count == 0)
        {
            BookmarkButton.Opacity = 0.55;
            return;
        }

        var page = _pages[_pageIndex];
        BookmarkButton.Opacity = _book.Bookmarks is { } bookmarks &&
                                 bookmarks.Any(bookmark =>
                                     bookmark.ChapterIndex == _chapterIndex &&
                                     bookmark.Offset == page.PositionOffset)
            ? 1
            : 0.55;
    }

    private void OnTocClick(object? sender, RoutedEventArgs e) => ShowReaderList(ReaderListMode.Toc);

    private void OnBookmarksClick(object? sender, RoutedEventArgs e) => ShowReaderList(ReaderListMode.Bookmarks);

    private void OnNotesClick(object? sender, RoutedEventArgs e) => ShowReaderList(ReaderListMode.Notes);

    private void OnReaderListCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseReaderList();
    }

    private void ShowReaderList(ReaderListMode mode)
    {
        if (_document is null || _book is null)
            return;

        _readerListMode = mode;
        UpdateReaderListTitle();
        RebuildReaderList();
        ReaderListOverlay.IsVisible = true;
        SetReaderChrome(false);
    }

    private void RebuildReaderList()
    {
        _readerListRows.Clear();
        ReaderListHost.ItemsSource = null;

        switch (_readerListMode)
        {
            case ReaderListMode.Toc:
                BuildTocList();
                break;
            case ReaderListMode.Bookmarks:
                BuildBookmarkList();
                break;
            case ReaderListMode.Notes:
                BuildAnnotationList();
                break;
        }

        ReaderListHost.ItemsSource = _readerListRows;

        if (_readerListMode == ReaderListMode.Toc && _chapterIndex >= 0)
        {
            var index = _chapterIndex;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReaderListOverlay.IsVisible && _readerListMode == ReaderListMode.Toc)
                    ReaderListHost.ScrollIntoView(index);
            }, DispatcherPriority.Background);
        }
    }

    private void ClearReaderList()
    {
        _readerListRows.Clear();
        ReaderListHost.ItemsSource = null;
    }

    private void RefreshReaderListDeferred()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReaderListOverlay.IsVisible)
                RebuildReaderList();
        }, DispatcherPriority.Background);
    }

    private void CloseReaderList()
    {
        ReaderListOverlay.IsVisible = false;
        _readerListMode = ReaderListMode.None;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReaderListOverlay.IsVisible)
                ClearReaderList();
        }, DispatcherPriority.Background);
    }

    private void UpdateReaderListTitle()
    {
        ReaderListTitle.Text = _readerListMode switch
        {
            ReaderListMode.Toc => L("TocTitle", "目录"),
            ReaderListMode.Bookmarks => L("BookmarksTitle", "书签"),
            _ => L("NotesTitle", "批注"),
        };
    }

    private void BuildTocList()
    {
        if (_document is null || _document.Chapters.Count == 0)
        {
            AddListEmpty(L("NoToc", "没有可用目录"));
            return;
        }

        for (var i = 0; i < _document.Chapters.Count; i++)
        {
            var chapterIndex = i;
            var title = $"{i + 1}. {_document.Chapters[i].Title}";
            AddListRow(
                title,
                "",
                () => JumpToChapter(chapterIndex),
                null,
                isCurrent: chapterIndex == _chapterIndex);
        }
    }

    private void BuildBookmarkList()
    {
        if (_book is null || _book.Bookmarks is null || _book.Bookmarks.Count == 0)
        {
            AddListEmpty(L("NoBookmarks", "还没有书签"));
            return;
        }

        foreach (var bookmark in _book.Bookmarks
                     .OrderBy(b => b.ChapterIndex)
                     .ThenBy(b => b.Offset)
                     .ToList())
        {
            var item = bookmark;
            var chapter = _document is not null &&
                          item.ChapterIndex >= 0 &&
                          item.ChapterIndex < _document.Chapters.Count
                ? _document.Chapters[item.ChapterIndex]
                : null;
            var percent = _document is not null && _document.TotalLength > 0
                ? (int)Math.Round(
                    (_document.GetChapterStart(item.ChapterIndex) + item.Offset) * 100.0 /
                    _document.TotalLength)
                : 0;
            var title = chapter?.Title ?? L("BookmarkFallback", "书签");
            AddListRow(
                title,
                $"{percent}%",
                () => JumpToBookmark(item),
                () =>
                {
                    if (_book is { } book)
                        _service.Library.Mutate(book, target => target.Bookmarks?.Remove(item));
                    _service.Library.Save();
                    UpdateBookmarkButtonState();
                    RefreshReaderListDeferred();
                });
        }
    }

    private void BuildAnnotationList()
    {
        if (_book is null || _book.Annotations is null || _book.Annotations.Count == 0)
        {
            AddListEmpty(L("NoNotes", "还没有批注"));
            return;
        }

        foreach (var annotation in _book.Annotations
                     .OrderBy(a => a.ChapterIndex)
                     .ThenBy(a => a.Start)
                     .ToList())
        {
            var item = annotation;
            var chapter = _document is not null &&
                          item.ChapterIndex >= 0 &&
                          item.ChapterIndex < _document.Chapters.Count
                ? _document.Chapters[item.ChapterIndex]
                : null;
            var snippet = "";
            if (chapter is not null)
            {
                var start = Math.Clamp(item.Start, 0, chapter.Text.Length);
                var end = Math.Clamp(item.End, start, chapter.Text.Length);
                snippet = chapter.Text[start..end].Replace('\n', ' ').Trim();
                if (snippet.Length > 60)
                    snippet = snippet[..60];
            }

            var meta = string.IsNullOrWhiteSpace(item.Note)
                ? snippet
                : $"{item.Note} · {snippet}";
            AddListRow(
                chapter?.Title ?? L("NotesTitle", "批注"),
                meta,
                () => JumpToAnnotation(item),
                () =>
                {
                    if (_book is { } book)
                        _service.Library.Mutate(book, target => target.Annotations?.Remove(item));
                    RefreshChapterAnnotations();
                    _service.Library.Save();
                    RenderPage();
                    RefreshReaderListDeferred();
                });
        }
    }

    private void AddListEmpty(string text)
    {
        _readerListRows.Add(new ReaderListRowModel { IsEmpty = true, Title = text });
    }

    private void AddListRow(
        string title,
        string meta,
        Action onOpen,
        Action? onDelete,
        bool isCurrent = false)
    {
        _readerListRows.Add(new ReaderListRowModel
        {
            Title = title,
            Meta = meta,
            Open = onOpen,
            Delete = onDelete,
            IsCurrent = isCurrent,
        });
    }

    private Control CreateReaderListRow(ReaderListRowModel? item)
    {
        if (item is null)
            return new Panel();

        if (item.IsEmpty)
        {
            return new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                Foreground = GetThemeBrush(ArcDesignTokenKind.TextSecondary, "#888888"),
                Margin = new Thickness(8, 12, 8, 0),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = item.IsCurrent ? $"● {item.Title}" : item.Title,
            FontSize = 13,
            FontWeight = item.IsCurrent ? FontWeight.Bold : FontWeight.SemiBold,
            Foreground = GetThemeBrush(ArcDesignTokenKind.Foreground, "#222222"),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(item.Meta))
        {
            content.Children.Add(new TextBlock
            {
                Text = item.Meta,
                FontSize = 11,
                Foreground = GetThemeBrush(ArcDesignTokenKind.TextSecondary, "#888888"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 42,
            });
        }

        var main = new TKCode.Arc.Button
        {
            Variant = ButtonVariant.Ghost,
            Size = ControlSize.Small,
            HorizontalAlignment = HAlign.Stretch,
            HorizontalContentAlignment = HAlign.Left,
            Padding = new Thickness(8, 6),
            Content = content,
        };
        main.Click += (_, _) => item.Open?.Invoke();
        row.Children.Add(main);

        if (item.Delete is not null)
        {
            var delete = new TKCode.Arc.Button
            {
                Variant = ButtonVariant.Ghost,
                Size = ControlSize.Small,
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HAlign.Center,
                VerticalContentAlignment = VAlign.Center,
                Content = new TextBlock
                {
                    Text = "✕",
                    FontSize = 14,
                    Foreground = GetThemeBrush(ArcDesignTokenKind.Danger, "#D14343"),
                },
            };
            Grid.SetColumn(delete, 1);
            delete.Click += (_, _) => item.Delete.Invoke();
            row.Children.Add(delete);
        }

        return row;
    }

    private IBrush GetThemeBrush(ArcDesignTokenKind kind, string fallback)
    {
        try
        {
            var color = ArcTokens.GetColor(this, kind);
            if (color.A > 0)
                return new SolidColorBrush(color);
        }
        catch
        {
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }

    private void JumpToChapter(int chapterIndex)
    {
        if (_document is null || chapterIndex < 0 || chapterIndex >= _document.Chapters.Count)
            return;

        CloseReaderList();
        SetReaderChrome(false);
        Paginate(chapterIndex, _document.GetChapterStart(chapterIndex));
    }

    private void JumpToBookmark(BookBookmark bookmark)
    {
        if (_document is null ||
            bookmark.ChapterIndex < 0 ||
            bookmark.ChapterIndex >= _document.Chapters.Count)
            return;

        CloseReaderList();
        SetReaderChrome(false);
        Paginate(
            bookmark.ChapterIndex,
            _document.GetChapterStart(bookmark.ChapterIndex) + Math.Max(0, bookmark.Offset));
    }

    private void JumpToAnnotation(BookAnnotation annotation)
    {
        if (_document is null ||
            annotation.ChapterIndex < 0 ||
            annotation.ChapterIndex >= _document.Chapters.Count)
            return;

        CloseReaderList();
        SetReaderChrome(false);
        Paginate(
            annotation.ChapterIndex,
            _document.GetChapterStart(annotation.ChapterIndex) + Math.Max(0, annotation.Start));
    }
}

internal sealed class ReaderListRowModel
{
    public string Title { get; init; } = "";

    public string Meta { get; init; } = "";

    public Action? Open { get; init; }

    public Action? Delete { get; init; }

    public bool IsEmpty { get; init; }

    public bool IsCurrent { get; init; }
}
