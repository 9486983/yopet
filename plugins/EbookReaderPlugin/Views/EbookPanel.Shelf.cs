using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EbookReaderPlugin.Core;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace EbookReaderPlugin.Views;

public partial class EbookPanel
{    private void RebuildShelf(bool reloadItems = true)
    {
        ShelfHost.Children.Clear();

        if (reloadItems)
        {
            foreach (var item in _shelfItems)
                item.Dispose();
            _shelfItems.Clear();

            var dirty = false;
            foreach (var book in _service.Library.GetBooks())
            {
                var loaded = LoadShelfCover(book);
                dirty |= loaded.Changed;
                _shelfItems.Add(new BookShelfItem(book, loaded.Cover, loaded.Accent));
            }

            if (dirty)
                _service.Library.Save();

            _shelfLoaded = true;
        }

        var isEmpty = _shelfItems.Count == 0;
        EmptyState.IsVisible = isEmpty;
        HeaderSubtitle.Text = isEmpty
            ? L("LibraryEmptySubtitle", "把电子书拖到宠物上导入")
            : string.Format(
                L("LibrarySubtitle", "{0} 本电子书 · 点击封面阅读"),
                _shelfItems.Count);
        LibraryFooter.Text = isEmpty
            ? L("LibraryEmpty", "书架还是空的")
            : L("LibraryFooter", "单击阅读 · 右键管理 · 滚轮浏览");

        if (isEmpty)
            return;

        if (_shelfMode == 0)
            BuildCoverShelves();
        else
            BuildSpineShelves();

        ShelfHost.Opacity = 0.35;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ShelfHost.Opacity = 1, DispatcherPriority.Background);
    }

    private (Bitmap? Cover, Color? Accent, bool Changed) LoadShelfCover(BookRecord book)
    {
        Color? accent = ParseCoverColor(book.CoverColor);
        Bitmap? cover = null;

        if (string.IsNullOrWhiteSpace(book.CoverPath) || !File.Exists(book.CoverPath))
            return (null, accent, false);

        try
        {
            if (book.CoverThumbnail)
            {
                cover = new Bitmap(book.CoverPath);

                if (accent is null)
                {
                    var thumbnail = CoverImageProcessor.CreateFromFile(book.CoverPath, computeColor: true);
                    if (thumbnail is not null && !string.IsNullOrWhiteSpace(thumbnail.ColorHex))
                    {
                        book.CoverColor = thumbnail.ColorHex;
                        accent = ParseCoverColor(thumbnail.ColorHex);
                        return (cover, accent, true);
                    }
                }

                return (cover, accent, false);
            }

            var migrated = CoverImageProcessor.CreateFromFile(book.CoverPath, computeColor: true);
            if (migrated is null)
                return (null, accent, false);

            var originalPath = book.CoverPath;
            var thumbnailPath = Path.ChangeExtension(originalPath, ".png");
            var temporaryPath = thumbnailPath + ".tmp";
            var changed = false;

            try
            {
                File.WriteAllBytes(temporaryPath, migrated.Png);
                File.Move(temporaryPath, thumbnailPath, overwrite: true);
                if (!string.Equals(thumbnailPath, originalPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(originalPath);
                    }
                    catch
                    {
                        // 原图删除失败时保留文件，不影响缩略图使用
                    }
                }

                book.CoverPath = thumbnailPath;
                book.CoverThumbnail = true;
                changed = true;
            }
            catch
            {
                // 写入失败时保持原图路径，避免把大图误标为缩略图
                book.CoverThumbnail = false;
                TryDeleteFile(temporaryPath);
            }

            if (!string.IsNullOrWhiteSpace(migrated.ColorHex))
            {
                if (!string.Equals(book.CoverColor, migrated.ColorHex, StringComparison.OrdinalIgnoreCase))
                {
                    book.CoverColor = migrated.ColorHex;
                    accent = ParseCoverColor(migrated.ColorHex);
                    changed = true;
                }
            }

            if (!File.Exists(book.CoverPath))
            {
                book.CoverThumbnail = false;
                return (null, accent, changed);
            }

            cover = new Bitmap(book.CoverPath);
            return (cover, accent, changed);
        }
        catch
        {
            return (null, accent, false);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static Color? ParseCoverColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Color.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private void BuildCoverShelves()
    {
        foreach (var row in _shelfItems.Chunk(CoverColumns))
        {
            var shell = CreateShelfShell(152);
            var host = (Grid)shell.Child!;
            var books = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Bottom,
                Margin = new Thickness(8, 8, 8, 12),
            };

            foreach (var item in row)
                books.Children.Add(CreateCoverItem(item));

            host.Children.Add(books);
            host.Children.Add(CreateShelfLedge());

            ShelfHost.Children.Add(shell);
        }
    }

    private void BuildSpineShelves()
    {
        foreach (var row in _shelfItems.Chunk(SpinesPerShelf))
        {
            var shell = CreateShelfShell(190);
            var host = (Grid)shell.Child!;
            var books = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Bottom,
                Margin = new Thickness(8, 8, 8, 10),
            };

            foreach (var item in row)
                books.Children.Add(CreateSpineItem(item));

            host.Children.Add(books);
            host.Children.Add(CreateShelfLedge());

            ShelfHost.Children.Add(shell);
        }
    }

    private static Border CreateShelfShell(double height)
    {
        var shell = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        ArcTokens.Bind(shell, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceRaised);
        ArcTokens.Bind(shell, Border.BorderBrushProperty, ArcDesignTokenKind.BorderSubtle);
        shell.Child = new Grid();
        return shell;
    }

    private static Grid CreateShelfLedge()
    {
        var ledge = new Grid
        {
            Height = 10,
            VerticalAlignment = VAlign.Bottom,
            Margin = new Thickness(8, 0, 8, 2),
        };

        var bar = new Border
        {
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 1, 0, 0),
        };
        ArcTokens.Bind(bar, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceMuted);

        var line = new Border
        {
            Height = 1,
            VerticalAlignment = VAlign.Top,
        };
        ArcTokens.Bind(line, Border.BackgroundProperty, ArcDesignTokenKind.BorderSubtle);

        ledge.Children.Add(bar);
        ledge.Children.Add(line);
        return ledge;
    }

    private Control CreateCoverItem(BookShelfItem item)
    {
        var holder = new Grid
        {
            Width = CoverWidth + 8,
            Height = CoverHeight + 8,
        };

        holder.Children.Add(new Border
        {
            Width = CoverWidth,
            Height = CoverHeight,
            CornerRadius = new CornerRadius(3),
            Background = PageShadowBrush,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Bottom,
            Margin = new Thickness(0, 4, 0, 0),
        });

        var cover = new Border
        {
            Width = CoverWidth,
            Height = CoverHeight,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HAlign.Left,
            VerticalAlignment = VAlign.Top,
            Child = CreateCoverContent(item),
        };

        holder.Children.Add(cover);
        AttachBookInteractions(holder, cover, item);
        return holder;
    }

    private Control CreateCoverContent(BookShelfItem item)
    {
        var cover = new Grid();

        if (item.Cover is { } bitmap)
        {
            cover.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
            });
        }
        else
        {
            var palette = PickPalette(item.Title, CoverPalettes);
            cover.Children.Add(new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(palette.Start, 0),
                        new GradientStop(palette.End, 1),
                    },
                },
            });

            var text = new StackPanel
            {
                VerticalAlignment = VAlign.Center,
                HorizontalAlignment = HAlign.Center,
                Margin = new Thickness(10, 10, 8, 16),
                Spacing = 4,
            };

            text.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#F5E9D4")),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxHeight = 54,
            });
            text.Children.Add(new TextBlock
            {
                Text = AuthorText(item),
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.Parse("#D7C2A2")),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = CoverWidth - 20,
            });

            cover.Children.Add(text);
        }

        cover.Children.Add(new Border
        {
            Width = 4,
            HorizontalAlignment = HAlign.Left,
            Background = new SolidColorBrush(Color.Parse("#44FFFFFF")),
        });
        cover.Children.Add(new Border
        {
            Width = 1,
            HorizontalAlignment = HAlign.Right,
            Background = new SolidColorBrush(Color.Parse("#33000000")),
        });

        var progress = new Grid
        {
            Height = 3,
            VerticalAlignment = VAlign.Bottom,
            Background = new SolidColorBrush(Color.Parse("#66000000")),
        };
        var fillWidth = Math.Clamp(CoverWidth * item.ProgressPercent / 100.0, 0, CoverWidth);
        if (fillWidth > 0.5)
        {
            progress.Children.Add(new Border
            {
                Width = fillWidth,
                HorizontalAlignment = HAlign.Left,
                Background = new SolidColorBrush(Color.Parse("#E8C17C")),
            });
        }

        cover.Children.Add(progress);
        return cover;
    }

    private Control CreateSpineItem(BookShelfItem item)
    {
        var hash = StableHash(item.Title);
        var fallbackPalette = PickPalette(item.Title, CoverPalettes);
        var (paletteStart, paletteEnd) = item.AccentColor is { } accent
            ? BuildSpineColors(accent)
            : fallbackPalette;
        var height = 126 + hash / 7 % 6 * 7;
        var width = 32 + hash / 11 % 4 * 4;
        var spineText = Luminance(paletteStart) > 0.55 ? SpineDarkTextBrush : SpineTextBrush;
        var bandsColor = Luminance(paletteStart) > 0.55
            ? Color.FromArgb(90, 60, 45, 30)
            : Color.FromArgb(120, 231, 194, 131);
        var bandsBrush = new SolidColorBrush(bandsColor);

        var spine = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VAlign.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(paletteEnd, 0),
                    new GradientStop(paletteStart, 0.42),
                    new GradientStop(paletteStart, 0.7),
                    new GradientStop(paletteEnd, 1),
                },
            },
        };

        var content = new Grid();
        content.Children.Add(new Border
        {
            Width = 3,
            HorizontalAlignment = HAlign.Left,
            Background = new SolidColorBrush(Color.Parse("#33FFFFFF")),
        });
        content.Children.Add(new Border
        {
            Width = 1,
            HorizontalAlignment = HAlign.Right,
            Background = new SolidColorBrush(Color.Parse("#44000000")),
        });
        content.Children.Add(new Border
        {
            Height = 2,
            VerticalAlignment = VAlign.Top,
            Margin = new Thickness(3, height * 0.16, 3, 0),
            Background = bandsBrush,
        });
        content.Children.Add(new Border
        {
            Height = 2,
            VerticalAlignment = VAlign.Bottom,
            Margin = new Thickness(3, 0, 3, height * 0.10),
            Background = bandsBrush,
        });
        content.Children.Add(new TextBlock
        {
            Text = FormatSpineTitle(item.Title),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = spineText,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 13,
            MaxWidth = width - 8,
            MaxHeight = height - 30,
            VerticalAlignment = VAlign.Center,
            HorizontalAlignment = HAlign.Center,
        });

        spine.Child = content;
        AttachBookInteractions(spine, spine, item);
        return spine;
    }

    private static (Color Start, Color End) BuildSpineColors(Color color)
    {
        var normalized = NormalizeForSpine(color);
        return (
            Mix(normalized, Colors.White, 0.08),
            Mix(normalized, Colors.Black, 0.4));
    }

    private static Color NormalizeForSpine(Color color)
    {
        var luminance = Luminance(color);
        if (luminance > 0.68)
            return Mix(color, Colors.Black, Math.Min(0.55, (luminance - 0.68) * 2.2));

        if (luminance < 0.25)
            return Mix(color, Colors.White, Math.Min(0.5, (0.25 - luminance) * 1.6));

        return color;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private void AttachBookInteractions(Control hoverTarget, Control clickTarget, BookShelfItem item)
    {
        clickTarget.Cursor = new Cursor(StandardCursorType.Hand);
        var progress = item.ProgressPercent <= 0 ? L("Unread", "未读") : item.ProgressText;
        ToolTip.SetTip(clickTarget, $"{item.Title}\n{AuthorText(item)} · {item.FormatLabel}\n{progress}");

        clickTarget.PointerPressed += async (_, e) =>
        {
            var point = e.GetCurrentPoint(clickTarget);
            if (point.Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                await OpenBookAsync(item.Book);
            }
        };

        var menu = new Avalonia.Controls.ContextMenu();
        var open = new Avalonia.Controls.MenuItem { Header = L("ContextOpen", "打开") };
        open.Click += async (_, _) => await OpenBookAsync(item.Book);
        var delete = new Avalonia.Controls.MenuItem { Header = L("ContextDelete", "删除") };
        delete.Click += async (_, _) => await DeleteBookAsync(item.Book);
        menu.ItemsSource = new object[] { open, delete };
        clickTarget.ContextMenu = menu;

        var hoverTransform = new TranslateTransform();
        hoverTransform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(120),
                Easing = new CubicEaseOut(),
            },
        };
        hoverTarget.RenderTransform = hoverTransform;

        hoverTarget.PointerEntered += (_, _) =>
        {
            hoverTransform.Y = -3;
            hoverTarget.ZIndex = 1;
        };
        hoverTarget.PointerExited += (_, _) =>
        {
            hoverTransform.Y = 0;
            hoverTarget.ZIndex = 0;
        };
    }

    private async Task DeleteBookAsync(BookRecord book)
    {
        try
        {
            var confirmed = await _host.ShowConfirmDialog(
                L("DeleteTitle", "删除书籍"),
                string.Format(
                    L("DeleteMessage", "确定从书架删除《{0}》吗？\n原始文件不会被删除。"),
                    book.Title));
            if (!confirmed)
                return;

            _service.Delete(book);
            RebuildShelf();
        }
        catch (Exception ex)
        {
            _host.ShowThought(L("DeleteTitle", "删除书籍"), ex.Message);
        }
    }

    private static T PickPalette<T>(string title, T[] palettes)
    {
        var index = StableHash(title) % palettes.Length;
        return palettes[index];
    }

    private string AuthorText(BookShelfItem item) =>
        string.IsNullOrWhiteSpace(item.Author) ? L("UnknownAuthor", "未知作者") : item.Author;

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value)
                hash = (hash ^ ch) * 16777619u;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private static string FormatSpineTitle(string title)
    {
        var text = title.Trim();
        if (text.Length == 0)
            return "无标题";

        if (text.Length > 18)
            text = text[..18];

        var cjk = text.Count(IsCjk);
        if (cjk < text.Length * 0.5)
            return text;

        return string.Join('\n', text.Where(ch => ch != ' ' && ch != '\t'));
    }

    private static bool IsCjk(char ch) =>
        ch is >= '\u3400' and <= '\u9FFF' or >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF';

    // ── 阅读器 ──

}
