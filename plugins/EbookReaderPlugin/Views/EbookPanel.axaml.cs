using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EbookReaderPlugin.Core;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using yopet.Sdk;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace EbookReaderPlugin.Views;

public partial class EbookPanel : UserControl
{
    private const string FontFamilyKey = "ebook.font_family";
    private const string FontSizeKey = "ebook.font_size";
    private const string LineSpacingKey = "ebook.line_spacing";
    private const string ThemeKey = "ebook.theme";
    private const string ShelfModeKey = "ebook.shelf_mode";
    private const string PageOpacityKey = "ebook.page_opacity";
    private const string CloseOnOutsideClickKey = "ebook.close_on_outside_click";
    private const string ShowImagesKey = "ebook.show_images";
    private const string GrayscaleImagesKey = "ebook.grayscale_images";
    private const double DialogInset = 24;

    private const int CoverColumns = 3;
    private const int SpinesPerShelf = 6;
    private const double CoverWidth = 80;
    private const double CoverHeight = 112;

    private static readonly IBrush PageShadowBrush = new SolidColorBrush(Color.Parse("#22000000"));
    private static readonly IBrush SpineTextBrush = new SolidColorBrush(Color.Parse("#F3E5C9"));
    private static readonly IBrush SpineDarkTextBrush = new SolidColorBrush(Color.Parse("#2E2318"));

    private static readonly (Color Start, Color End)[] CoverPalettes =
    {
        (Color.Parse("#9A4A3A"), Color.Parse("#4E1F19")),
        (Color.Parse("#345A7A"), Color.Parse("#172C40")),
        (Color.Parse("#5B6E43"), Color.Parse("#29351C")),
        (Color.Parse("#8A6634"), Color.Parse("#453015")),
        (Color.Parse("#654274"), Color.Parse("#301E3B")),
        (Color.Parse("#356E6A"), Color.Parse("#173B38")),
        (Color.Parse("#7A3E2C"), Color.Parse("#3E1D13")),
        (Color.Parse("#4B5168"), Color.Parse("#222634")),
    };



    private readonly IPluginHost _host;
    private readonly EbookService _service;
    private readonly Func<string, string> _localize;
    private readonly List<PageTextSlice> _pages = new();
    private readonly List<BookShelfItem> _shelfItems = new();
    private readonly List<ReaderListRowModel> _readerListRows = new();
    private readonly List<BookAnnotation> _chapterAnnotations = new();
    private readonly Dictionary<string, IBrush> _annotationBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<BookImage, Bitmap> _imageCache = new();
    private readonly Dictionary<BookImage, Bitmap> _grayscaleImageCache = new();
    private readonly Dictionary<BookImage, (int Width, int Height)> _imageSizeCache = new();
    private readonly CancellationTokenSource _lifetime = new();

    private Window? _readerWindow;
    private bool _suppressDeactivateClose;
    private bool _readerClosing;
    private bool _lifetimeDisposed;
    private TopLevel? _topLevel;

    private BookRecord? _autoOpen;
    private EbookDocument? _document;
    private BookRecord? _book;
    private int _chapterIndex;
    private int _pageIndex;
    private double _fontSize = 16;
    private double _lineSpacing = 1.7;
    private double _pageOpacity = 0.9;
    private bool _closeOnOutsideClick = true;
    private bool _suppressCloseOnOutside;
    private bool _showImages = true;
    private bool _grayscaleImages;
    private bool _suppressImageMenu;
    private bool _suppressImageSettings;
    private string? _fontFamilyName;
    private int _themeIndex;
    private int _shelfMode;
    private bool _busy;
    private bool _paginating;
    private bool _suppressSettings;
    private bool _suppressShelfMode;
    private bool _settingsOpen;
    private bool _chromeVisible = true;
    private bool _fontListLoaded;
    private bool _shelfLoaded;
    private long? _pendingRestore;
    private double _lastTextWidth;
    private double _lastTextHeight;
    private double _wheelAccumulator;
    private DateTime _lastWheelTurnUtc;
    private readonly TranslateTransform _pageTransform = new();
    private readonly TranslateTransform _readerFooterTransform = new();
    private readonly DispatcherTimer _chromeHideTimer;
    private readonly DispatcherTimer _configSaveTimer;
    private int _selectionStart;
    private int _selectionEnd;
    private ReaderListMode _readerListMode;

    /// <summary>供 Avalonia XAML 编译器/设计器使用；运行时请调用带参数的构造函数。</summary>
    public EbookPanel()
    {
        InitializeComponent();
        ImageSettingsPopup.PlacementTarget = ImageMenuAction;
        _host = null!;
        _service = null!;
        _localize = _ => "";
        _chromeHideTimer = null!;
        _configSaveTimer = null!;
    }

    public EbookPanel(
        IPluginHost host,
        EbookService service,
        BookRecord? autoOpen = null,
        Func<string, string>? localize = null)
    {
        _host = host;
        _service = service;
        _autoOpen = autoOpen;
        _localize = localize ?? (_ => "");
        _chromeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _chromeHideTimer.Tick += (_, _) =>
        {
            _chromeHideTimer.Stop();
            if (!_chromeVisible ||
                ReaderListOverlay.IsVisible ||
                SelectionToolbar.IsVisible ||
                ImageSettingsPopup.IsOpen)
                return;

            SetReaderChrome(false);
        };

        _configSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _configSaveTimer.Tick += (_, _) =>
        {
            _configSaveTimer.Stop();
            FlushPendingSettings();
        };

        InitializeComponent();
        ImageSettingsPopup.PlacementTarget = ImageMenuAction;
        ReaderListHost.ItemTemplate = new FuncDataTemplate<ReaderListRowModel>(
            (row, _) => CreateReaderListRow(row),
            supportsRecycling: false);
        DialogChrome.SetInset(this, DialogInset);
        DialogChrome.SetHostDragBar(this, false);
        DialogChrome.SetButtonInset(this, 10);

        PageText.AddHandler(InputElement.PointerReleasedEvent, OnPageTextPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        PageFrame.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPageFramePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        var quickFade = TimeSpan.FromMilliseconds(140);
        _pageTransform.Transitions = new Transitions
        {
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = quickFade, Easing = new CubicEaseOut() },
        };
        PageText.RenderTransform = _pageTransform;
        ShelfHost.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
        };
        HeaderPanel.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = quickFade, Easing = new CubicEaseOut() },
        };
        ReaderFooter.RenderTransform = _readerFooterTransform;
        ReaderFooter.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
        };

        LoadSettings();
        ApplyLocalization();
        ApplyTheme();

        _host.LanguageChanged += OnLanguageChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel?.RemoveHandler(
            InputElement.PointerWheelChangedEvent,
            OnTopLevelWheel);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnTopLevelWheel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        if (_topLevel is Window window)
        {
            _readerWindow = window;
            window.Deactivated += OnReaderWindowDeactivated;
        }

        Focus();

        if (_autoOpen is { } book)
        {
            _autoOpen = null;
            Dispatcher.UIThread.Post(() => _ = OpenBookAsync(book), DispatcherPriority.Loaded);
            return;
        }

        if (!_shelfLoaded)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_shelfLoaded && !ReaderPanel.IsVisible)
                    RebuildShelf();
            }, DispatcherPriority.Background);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FlushPendingSettings();

        if (!_readerClosing)
        {
            SaveCurrentProgress();
            _service?.Flush();
        }

        _chromeHideTimer?.Stop();
        _configSaveTimer?.Stop();
        if (!_lifetimeDisposed)
        {
            _lifetimeDisposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        if (_host is not null)
            _host.LanguageChanged -= OnLanguageChanged;

        _topLevel?.RemoveHandler(
            InputElement.PointerWheelChangedEvent,
            OnTopLevelWheel);
        if (_readerWindow is not null)
            _readerWindow.Deactivated -= OnReaderWindowDeactivated;
        _readerWindow = null;
        _topLevel = null;

        ShelfHost.Children.Clear();
        foreach (var item in _shelfItems)
            item.Dispose();
        _shelfItems.Clear();
        _shelfLoaded = false;

        PageText.Inlines?.Clear();
        CloseImageSettingsMenu();
        ClearImageCache();
        ClearReaderList();
        _pages.Clear();
        _chapterAnnotations.Clear();
        _annotationBrushCache.Clear();
        _document = null;
        _book = null;

        base.OnDetachedFromVisualTree(e);
    }

    // ── 设置读写 ──

    private void LoadSettings()
    {
        var size = _host.GetConfig(FontSizeKey);
        if (double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize))
            _fontSize = Math.Clamp(parsedSize, 12, 28);

        var spacing = _host.GetConfig(LineSpacingKey);
        if (double.TryParse(spacing, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSpacing))
            _lineSpacing = Math.Clamp(parsedSpacing, 1.4, 2.0);

        var family = _host.GetConfig(FontFamilyKey);
        if (!string.IsNullOrWhiteSpace(family))
            _fontFamilyName = family.Trim();

        var theme = _host.GetConfig(ThemeKey);
        if (int.TryParse(theme, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTheme))
            _themeIndex = Math.Clamp(parsedTheme, 0, 2);

        var opacity = _host.GetConfig(PageOpacityKey);
        if (double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOpacity))
            _pageOpacity = Math.Clamp(parsedOpacity, 0.1, 1.0);

        var closeOnOutside = _host.GetConfig(CloseOnOutsideClickKey);
        if (bool.TryParse(closeOnOutside, out var parsedCloseOnOutside))
            _closeOnOutsideClick = parsedCloseOnOutside;

        var showImages = _host.GetConfig(ShowImagesKey);
        if (bool.TryParse(showImages, out var parsedShowImages))
            _showImages = parsedShowImages;

        var grayscaleImages = _host.GetConfig(GrayscaleImagesKey);
        if (bool.TryParse(grayscaleImages, out var parsedGrayscaleImages))
            _grayscaleImages = parsedGrayscaleImages;

        var shelfMode = _host.GetConfig(ShelfModeKey);
        if (int.TryParse(shelfMode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedShelfMode))
            _shelfMode = Math.Clamp(parsedShelfMode, 0, 1);
    }

    private static readonly Lazy<IReadOnlyList<string>> SystemFontNames = new(LoadSystemFontNames);

    private static IReadOnlyList<string> LoadSystemFontNames()
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(font => font.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void SaveSetting(string key, string value) => _host.SetConfig(key, value);

    private void SaveSettingDebounced(string key, string value)
    {
        _pendingSettings[key] = value;
        _configSaveTimer?.Stop();
        _configSaveTimer?.Start();
    }

    private void FlushPendingSettings()
    {
        if (_host is null || _pendingSettings.Count == 0)
            return;

        var values = _pendingSettings.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        _pendingSettings.Clear();
        _host.SaveConfigValues(values);
    }

    private string L(string key, string fallback)
    {
        try
        {
            var value = _localize(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private void ApplyLocalization()
    {
        ImportButtonText.Text = L("ImportButton", "导入");
        EmptyImportButton.Content = L("ImportEmptyButton", "导入电子书");
        EmptyTitleText.Text = L("LibraryEmpty", "书架还是空的");
        EmptyHintText.Text = L("EmptyHint", "把电子书拖到宠物上，或点击下方按钮导入");
        LibraryFooter.Text = L("LibraryFooter", "单击阅读 · 右键管理 · 滚轮浏览");

        FontLabelText.Text = L("FontLabel", "字体");
        FontSizeLabelText.Text = L("FontSizeLabel", "字号");
        LineSpacingLabelText.Text = L("LineSpacingLabel", "行距");
        ThemeLabelText.Text = L("ThemeLabel", "主题");
        ShowImagesLabel.Text = L("ShowImagesLabel", "展示图片");
        GrayscaleImagesLabel.Text = L("GrayscaleImagesLabel", "默认黑白");
        UpdateCloseOnOutsideButton();
        UpdateImageSettingsButtons();

        HighlightSelectionButton.Content = L("Highlight", "高亮");
        AnnotateSelectionButton.Content = L("Annotate", "批注");
        CancelSelectionButton.Content = L("CancelSelection", "取消");
        if (ReaderListOverlay.IsVisible)
            UpdateReaderListTitle();

        if (!ReaderPanel.IsVisible)
            HeaderTitle.Text = L("LibraryTitle", "书架");

        InitializeShelfMode();
        InitializeSettingsUi();
        UpdateOpacityText();
    }

    private void OnLanguageChanged(object? sender, string cultureName)
    {
        _fontListLoaded = false;
        if (_settingsOpen)
            FontSelect.ItemsSource = null;

        ApplyLocalization();

        if (ReaderPanel.IsVisible)
        {
            if (_book is not null)
                ReaderTitle.Text = _book.Title;
            if (_document is not null &&
                _chapterIndex >= 0 &&
                _chapterIndex < _document.Chapters.Count)
                ReaderChapterTitle.Text = _document.Chapters[_chapterIndex].Title;
        }
        else
        {
            RebuildShelf(reloadItems: false);
        }

        if (_settingsOpen)
            EnsureFontList();
    }

    private void InitializeShelfMode()
    {
        ShelfModeControl.ItemsSource = new[]
        {
            new SegmentedOption("cover", L("ShelfModeCover", "封面"), null),
            new SegmentedOption("spine", L("ShelfModeSpine", "书脊"), null),
        };

        _suppressShelfMode = true;
        try
        {
            ShelfModeControl.SelectedIndex = _shelfMode;
        }
        finally
        {
            _suppressShelfMode = false;
        }
    }

    private void InitializeSettingsUi()
    {
        _suppressSettings = true;
        try
        {
            FontSizeText.Text = ((int)Math.Round(_fontSize)).ToString(CultureInfo.InvariantCulture);

            LineSpacingSelect.ItemsSource = new[]
            {
                new SelectOption("1.4", L("LineSpacingCompact", "紧凑")),
                new SelectOption("1.7", L("LineSpacingNormal", "标准")),
                new SelectOption("2.0", L("LineSpacingLoose", "宽松")),
            };
            LineSpacingSelect.SelectedValue = _lineSpacing switch
            {
                <= 1.55 => "1.4",
                >= 1.85 => "2.0",
                _ => "1.7",
            };

            ThemeSelect.ItemsSource = new[]
            {
                new SelectOption(0, L("ThemePaper", "纸黄")),
                new SelectOption(1, L("ThemeWhite", "白纸")),
                new SelectOption(2, L("ThemeNight", "夜间")),
            };
            ThemeSelect.SelectedValue = _themeIndex;
            UpdateImageSettingsButtons();
        }
        finally
        {
            _suppressSettings = false;
        }
    }

    private void EnsureFontList()
    {
        if (_fontListLoaded)
            return;

        var names = new List<string> { L("SystemDefault", "系统默认") };
        names.AddRange(SystemFontNames.Value);

        if (!string.IsNullOrWhiteSpace(_fontFamilyName) &&
            !names.Contains(_fontFamilyName, StringComparer.OrdinalIgnoreCase))
            names.Add(_fontFamilyName);

        _suppressSettings = true;
        try
        {
            FontSelect.ItemsSource = names
                .Select(name => new SelectOption(name, name))
                .ToArray();
            FontSelect.SelectedValue = string.IsNullOrWhiteSpace(_fontFamilyName)
                ? L("SystemDefault", "系统默认")
                : _fontFamilyName;
            _fontListLoaded = true;
        }
        finally
        {
            _suppressSettings = false;
        }
    }

    private void ApplyTypography()
    {
        var fontFamily = string.IsNullOrWhiteSpace(_fontFamilyName)
            ? FontFamily.Default
            : new FontFamily(_fontFamilyName);

        PageText.FontFamily = fontFamily;
        PageText.FontSize = _fontSize;
        PageText.LineHeight = _fontSize * _lineSpacing;

        var headingSize = Math.Clamp(_fontSize + 4, 16, 32);
        PageChapterHeading.FontFamily = fontFamily;
        PageChapterHeading.FontSize = headingSize;
        PageChapterHeading.LineHeight = headingSize * 1.5;
    }

    private void ApplyTypographyAndRepaginate()
    {
        if (_document is null)
        {
            ApplyTypography();
            return;
        }

        var restore = CurrentGlobalOffset();
        ApplyTypography();
        if (ReaderPanel.IsVisible)
            Paginate(_chapterIndex, restore);
    }

    private void ApplyTheme()
    {
        var (background, foreground) = _themeIndex switch
        {
            1 => ("#FFFFFF", "#23252A"),
            2 => ("#1B1D22", "#D6D0C4"),
            _ => ("#FFFDF8", "#2B2620"),
        };

        var pageColor = Color.Parse(background);
        var foregroundColor = Color.Parse(foreground);
        var foregroundBrush = new SolidColorBrush(foregroundColor);
        var alpha = (byte)Math.Round(Math.Clamp(_pageOpacity, 0.1, 1.0) * 255);
        PageFrame.Background = Brushes.Transparent;
        PageFrame.BorderThickness = new Thickness(0);
        PageFrame.CornerRadius = new CornerRadius(0);
        PageText.Foreground = foregroundBrush;
        PageChapterHeading.Foreground = foregroundBrush;
        PageText.SelectionBrush = CreateSelectionBrush();

        if (_document is not null)
        {
            RootBorder.Background = new SolidColorBrush(
                Color.FromArgb(alpha, pageColor.R, pageColor.G, pageColor.B));
        }

        var zoneBrush = Brush.Parse(_themeIndex == 2 ? "#8A8F9A" : "#8B6B3E");
        LeftZoneHint.Foreground = zoneBrush;
        RightZoneHint.Foreground = zoneBrush;
        UpdateOpacityText();
    }

    private IBrush CreateSelectionBrush()
    {
        var color = Color.Parse("#3B82F6");
        try
        {
            var accent = ArcTokens.GetColor(this, ArcDesignTokenKind.Accent);
            if (accent.A > 0)
                color = accent;
        }
        catch
        {
        }

        return new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
    }

    private async Task OpenBookAsync(BookRecord? book, EbookDocument? parsedDocument = null)
    {
        if (book is null || _busy)
            return;

        _busy = true;
        HeaderSubtitle.Text = L("Opening", "正在打开…");

        try
        {
            var document = parsedDocument is null
                ? await _service.OpenAsync(book, _lifetime.Token)
                : await _service.OpenAsync(book, parsedDocument, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;

            var location = document.Locate(book.Position);

            ClearImageCache();
            PageImageFrame.IsVisible = false;
            PageText.Opacity = 1;
            PageText.IsHitTestVisible = true;
            _document = document;
            _book = book;
            _chapterIndex = location.ChapterIndex;
            _pageIndex = 0;
            _pages.Clear();
            _pendingRestore = book.Position;

            ShowReader();
            SchedulePagination(book.Position);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HeaderSubtitle.Text = L("OpenFailed", "打开失败");
            _host.ShowThought(L("OpenFailedTitle", "电子书打开失败"), ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || _busy)
            return;

        _busy = true;
        HeaderSubtitle.Text = L("Importing", "正在导入…");

        BookRecord? toOpen = null;
        EbookDocument? toOpenDocument = null;

        try
        {
            var result = await _service.ImportAsync(paths, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;

            RebuildShelf();

            if (result.Errors.Count > 0)
            {
                HeaderSubtitle.Text = result.Errors[0];
                _host.ShowThought(L("ImportFailedTitle", "电子书导入失败"), string.Join("\n", result.Errors));
            }
            else if (result.Imported.Count == 0 && result.Skipped.Count > 0)
            {
                HeaderSubtitle.Text = L("AlreadyInShelf", "这些书已经在书架中了");
            }
            else
            {
                HeaderSubtitle.Text = string.Format(
                    L("ImportedCount", "已导入 {0} 本"),
                    result.Imported.Count);
                _host.ShowReaction("📚");
            }

            if (result.Imported.Count == 1)
            {
                toOpen = result.Imported[0];
                result.Documents.TryGetValue(toOpen.Id, out toOpenDocument);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HeaderSubtitle.Text = ex.Message;
            _host.ShowThought(L("ImportFailedTitle", "电子书导入失败"), ex.Message);
        }
        finally
        {
            _busy = false;
        }

        if (toOpen is not null)
            await OpenBookAsync(toOpen, toOpenDocument);
    }

    private void ShowReader()
    {
        if (_document is null || _book is null)
            return;

        CloseImageSettingsMenu();
        LibraryPanel.IsVisible = false;
        ReaderPanel.IsVisible = true;
        LibraryFooter.IsVisible = false;
        SettingsStrip.IsVisible = false;
        _settingsOpen = false;
        _chromeVisible = false;
        _chromeHideTimer.Stop();
        HeaderPanel.IsVisible = false;
        ReaderHeaderOverlay.IsVisible = true;
        ReaderFooter.IsVisible = false;
        ReaderStatusBar.IsVisible = true;
        ReaderHeaderOverlay.Opacity = 1;
        _readerFooterTransform.Y = 8;
        ReaderFooter.Opacity = 0;
        ReaderListOverlay.IsVisible = false;
        SelectionToolbar.IsVisible = false;
        PageImageFrame.IsVisible = false;
        PageText.Opacity = 1;
        PageText.IsHitTestVisible = true;
        RootContent.Margin = new Thickness(10, 0, 10, 8);
        BackButton.IsVisible = true;
        ShelfModeControl.IsVisible = false;
        ImportButton.IsVisible = false;
        ReaderTitle.Text = _book.Title;
        ReaderChapterTitle.Text = _document.Chapters.Count > 0
            ? _document.Chapters[_chapterIndex].Title
            : "";

        ApplyTypography();
        ApplyTheme();

        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    private void ShowLibrary(bool restoreFocus = true)
    {
        SaveCurrentProgress();
        _service?.Flush();
        CloseImageSettingsMenu();
        ClearImageCache();
        PageImageFrame.IsVisible = false;
        PageText.Opacity = 1;
        PageText.IsHitTestVisible = true;
        _document = null;
        _book = null;
        _pages.Clear();
        _chapterAnnotations.Clear();
        _chapterIndex = 0;
        _pageIndex = 0;
        _pendingRestore = null;
        _chromeVisible = true;
        _chromeHideTimer.Stop();

        ReaderPanel.IsVisible = false;
        PageChapterHeading.IsVisible = false;
        ReaderFooter.IsVisible = false;
        ReaderStatusBar.IsVisible = false;
        ReaderHeaderOverlay.IsVisible = false;
        _readerFooterTransform.Y = 8;
        ReaderFooter.Opacity = 0;
        ReaderHeaderOverlay.Opacity = 1;
        ReaderListOverlay.IsVisible = false;
        ClearReaderList();
        SelectionToolbar.IsVisible = false;
        _selectionStart = 0;
        _selectionEnd = 0;
        LibraryPanel.IsVisible = true;
        LibraryFooter.IsVisible = true;
        HeaderPanel.IsVisible = true;
        HeaderPanel.Opacity = 1;
        ArcTokens.Bind(RootBorder, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceRaised);
        ArcTokens.Bind(RootBorder, Border.BorderBrushProperty, ArcDesignTokenKind.Border);
        RootBorder.BorderThickness = new Thickness(1);
        RootContent.Margin = new Thickness(14, 0, 14, 12);
        SettingsStrip.IsVisible = false;
        _settingsOpen = false;
        BackButton.IsVisible = false;
        ShelfModeControl.IsVisible = true;
        ImportButton.IsVisible = true;
        HeaderTitle.Text = L("LibraryTitle", "书架");
        RebuildShelf(reloadItems: !_shelfLoaded);
        if (restoreFocus)
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    private void ToggleReaderChrome()
    {
        if (_document is null)
            return;

        SetReaderChrome(!_chromeVisible);
        Focus();
    }

    private void SetReaderChrome(bool visible)
    {
        if (visible)
        {
            _chromeVisible = true;
            _chromeHideTimer.Stop();
            _readerFooterTransform.Y = 8;
            ReaderFooter.Opacity = 0;
            ReaderFooter.IsVisible = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_chromeVisible)
                    return;

                _readerFooterTransform.Y = 0;
                ReaderFooter.Opacity = 1;
            }, DispatcherPriority.Background);
            return;
        }

        _chromeVisible = false;
        _chromeHideTimer.Stop();
        _settingsOpen = false;
        SettingsStrip.IsVisible = false;
        CloseImageSettingsMenu();
        _readerFooterTransform.Y = 8;
        ReaderFooter.Opacity = 0;
        _ = HideReaderChromeAsync();
    }

    private async Task HideReaderChromeAsync()
    {
        await Task.Delay(150);
        if (_chromeVisible || _lifetime.IsCancellationRequested)
            return;

        ReaderFooter.IsVisible = false;
        ReaderFooter.Opacity = 0;
    }

    private void OnReaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible)
            return;

        if (ImageSettingsPopup.IsOpen)
        {
            _chromeHideTimer.Stop();
            return;
        }

        if (ReaderListOverlay.IsVisible || SelectionToolbar.IsVisible)
            return;

        if (e.GetCurrentPoint(RootBorder).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(RootBorder);
        var nearEdge = position.Y >= RootBorder.Bounds.Height - 32;
        var overChrome = ReaderFooter.IsPointerOver || ReaderStatusBar.IsPointerOver;

        if (nearEdge || overChrome)
        {
            _chromeHideTimer.Stop();
            if (!_chromeVisible)
                SetReaderChrome(true);
            return;
        }

        if (_chromeVisible)
        {
            _chromeHideTimer.Stop();
            _chromeHideTimer.Start();
        }
    }

    private void UpdateOpacityText()
    {
        if (PageOpacityText is null)
            return;

        PageOpacityText.Text = string.Format(
            L("OpacityHint", "窗体透明度 {0}% · Ctrl + 滚轮调整"),
            (int)Math.Round(Math.Clamp(_pageOpacity, 0.1, 1.0) * 100));
    }

    // ── 分页与渲染 ──

    private void SchedulePagination(long restorePosition, int attempt = 0)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_document is null || !ReaderPanel.IsVisible)
                return;

            if (PageContentGrid.Bounds.Width < 40 || PageContentGrid.Bounds.Height < 40)
            {
                if (attempt < 8)
                    SchedulePagination(restorePosition, attempt + 1);
                return;
            }

            Paginate(_chapterIndex, restorePosition);
        }, DispatcherPriority.Loaded);
    }

    private void Paginate(int chapterIndex, long restorePosition)
    {
        if (_document is null || _paginating)
            return;

        if (chapterIndex < 0 || chapterIndex >= _document.Chapters.Count)
            return;

        if (PageContentGrid.Bounds.Width < 40 || PageContentGrid.Bounds.Height < 40)
            return;

        _paginating = true;
        try
        {
            _chapterIndex = chapterIndex;
            RefreshChapterAnnotations();
            var chapter = _document.Chapters[chapterIndex];
            var chapterTitle = GetChapterTitle(chapterIndex, chapter);
            var fontFamily = string.IsNullOrWhiteSpace(_fontFamilyName)
                ? null
                : new FontFamily(_fontFamilyName);

            _pages.Clear();
            _pages.AddRange(PaginationEngine.PaginateChapter(
                chapter.Text,
                PageContentGrid.Bounds.Width,
                PageContentGrid.Bounds.Height,
                fontFamily,
                _fontSize,
                _lineSpacing,
                GetChapterHeadingHeight(chapterTitle),
                _showImages ? chapter.Images : Array.Empty<BookImage>(),
                image => GetImageNaturalHeight(image, PageContentGrid.Bounds.Width)));

            var chapterStart = _document.GetChapterStart(chapterIndex);
            var offset = (int)Math.Clamp(restorePosition - chapterStart, 0, chapter.Text.Length);
            var pageIndex = _pages.FindLastIndex(page => page.PositionOffset <= offset);
            _pageIndex = pageIndex < 0 ? 0 : Math.Min(pageIndex, _pages.Count - 1);

            RenderPage();
        }
        finally
        {
            _paginating = false;
        }
    }

    private void RenderPage()
    {
        if (_document is null || _pages.Count == 0)
            return;

        if (SelectionToolbar.IsVisible)
            HideSelectionToolbar();

        _pageIndex = Math.Clamp(_pageIndex, 0, _pages.Count - 1);
        var page = _pages[_pageIndex];
        var chapter = _document.Chapters[_chapterIndex];
        var chapterTitle = GetChapterTitle(_chapterIndex, chapter);
        var showHeading = _pageIndex == 0 && page.Image is null;

        PageChapterHeading.Text = chapterTitle;
        PageChapterHeading.IsVisible = showHeading;
        _pageTransform.Y = showHeading ? GetChapterHeadingHeight(chapterTitle) : 0;
        _pageTransform.X = 8;
        var hasPageText = page.Text.Trim().Length > 0;
        if (page.Image is { } image)
        {
            PageImageFrame.IsVisible = true;
            PageImage.Source = GetImageBitmap(image);
            PageImage.MaxWidth = PageContentGrid.Bounds.Width;
            PageImage.MaxHeight = page.ImageHeight > 0
                ? page.ImageHeight
                : PageContentGrid.Bounds.Height;
        }
        else
        {
            PageImageFrame.IsVisible = false;
            PageImage.Source = null;
        }

        if (hasPageText)
        {
            PageText.Opacity = 1;
            PageText.IsHitTestVisible = true;
            SetPageInlines(page);
        }
        else
        {
            PageText.Opacity = 0;
            PageText.IsHitTestVisible = false;
            PageText.SelectionStart = 0;
            PageText.SelectionEnd = 0;
            PageText.Inlines?.Clear();
        }
        ReaderChapterTitle.Text = chapterTitle;
        Dispatcher.UIThread.Post(() => _pageTransform.X = 0, DispatcherPriority.Background);
        PageInfoText.Text = $"{_pageIndex + 1} / {_pages.Count}";
        _pendingRestore = null;

        var globalOffset = _document.GetChapterStart(_chapterIndex) + page.PositionOffset;
        var total = _document.TotalLength;
        var percent = total <= 0
            ? 0
            : Math.Clamp((int)Math.Round(globalOffset * 100.0 / total), 0, 100);
        ProgressText.Text = $"{percent}%";

        UpdateNavigationState();
        UpdateBookmarkButtonState();
        SaveProgress(globalOffset, total);
    }

    private string GetChapterTitle(int chapterIndex, Chapter chapter) =>
        string.IsNullOrWhiteSpace(chapter.Title)
            ? $"第 {chapterIndex + 1} 章"
            : chapter.Title;

    private double GetChapterHeadingHeight(string? chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return 0;

        var headingSize = Math.Clamp(_fontSize + 4, 16, 32);
        return headingSize * 1.5 + 8;
    }

    private double GetImageNaturalHeight(BookImage image, double width)
    {
        var size = GetImagePixelSize(image);
        if (size is null || size.Value.Width <= 0)
            return Math.Max(1, _fontSize * _lineSpacing);

        var scale = Math.Min(1.0, Math.Max(1, width) / size.Value.Width);
        return Math.Max(1, size.Value.Height * scale);
    }

    private (int Width, int Height)? GetImagePixelSize(BookImage image)
    {
        if (_imageSizeCache.TryGetValue(image, out var cached))
            return cached;

        var size = EbookImageProcessor.GetPixelSize(image.Data);
        if (size is not null)
            _imageSizeCache[image] = size.Value;
        return size;
    }

    private Bitmap? GetImageBitmap(BookImage image)
    {
        var cache = _grayscaleImages ? _grayscaleImageCache : _imageCache;
        if (cache.TryGetValue(image, out var cached))
            return cached;

        try
        {
            var data = _grayscaleImages ? EbookImageProcessor.ToGrayscale(image.Data) : image.Data;
            if (data is not { Length: > 0 })
                data = image.Data;
            if (data is not { Length: > 0 })
                return null;

            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            cache[image] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ClearImageCache()
    {
        PageImage.Source = null;
        foreach (var bitmap in _imageCache.Values)
            bitmap.Dispose();
        foreach (var bitmap in _grayscaleImageCache.Values)
            bitmap.Dispose();
        _imageCache.Clear();
        _grayscaleImageCache.Clear();
        _imageSizeCache.Clear();
    }

    private long CurrentGlobalOffset()
    {
        if (_document is null)
            return _book?.Position ?? 0;

        if (_pages.Count == 0 || _pageIndex < 0 || _pageIndex >= _pages.Count)
            return _pendingRestore ?? _document.GetChapterStart(_chapterIndex);

        return _document.GetChapterStart(_chapterIndex) + _pages[_pageIndex].PositionOffset;
    }

    private void SaveCurrentProgress()
    {
        if (_document is null || _book is null || _pages.Count == 0)
            return;

        SaveProgress(CurrentGlobalOffset(), _document.TotalLength);
    }

    private void SaveProgress(long position, long totalLength)
    {
        if (_book is null)
            return;

        _service.SaveProgress(_book, position, totalLength);
    }

    private void UpdateNavigationState()
    {
        if (_document is null)
            return;

        var hasPreviousChapter = _chapterIndex > 0;
        var hasNextChapter = _chapterIndex < _document.Chapters.Count - 1;

        PrevChapterButton.IsEnabled = hasPreviousChapter;
        NextChapterButton.IsEnabled = hasNextChapter;
        PrevPageButton.IsEnabled = _pages.Count > 0 && (_pageIndex > 0 || hasPreviousChapter);
        NextPageButton.IsEnabled = _pages.Count > 0 && (_pageIndex < _pages.Count - 1 || hasNextChapter);
    }

    private void GoToNextPage()
    {
        if (_document is null || _pages.Count == 0)
            return;

        if (_pageIndex < _pages.Count - 1)
        {
            _pageIndex++;
            RenderPage();
        }
        else if (_chapterIndex < _document.Chapters.Count - 1)
        {
            var next = _chapterIndex + 1;
            Paginate(next, _document.GetChapterStart(next));
        }

        Focus();
    }

    private void GoToPreviousPage()
    {
        if (_document is null || _pages.Count == 0)
            return;

        if (_pageIndex > 0)
        {
            _pageIndex--;
            RenderPage();
        }
        else if (_chapterIndex > 0)
        {
            var previous = _chapterIndex - 1;
            var chapter = _document.Chapters[previous];
            Paginate(previous, _document.GetChapterStart(previous) + chapter.Text.Length);
        }

        Focus();
    }

    private void GoToNextChapter()
    {
        if (_document is null || _chapterIndex >= _document.Chapters.Count - 1)
            return;

        var next = _chapterIndex + 1;
        Paginate(next, _document.GetChapterStart(next));
        Focus();
    }

    private void GoToPreviousChapter()
    {
        if (_document is null || _chapterIndex <= 0)
            return;

        var previous = _chapterIndex - 1;
        Paginate(previous, _document.GetChapterStart(previous));
        Focus();
    }

    private void GoToBookStart()
    {
        if (_document is null || _document.Chapters.Count == 0)
            return;

        Paginate(0, 0);
        Focus();
    }

    private void GoToBookEnd()
    {
        if (_document is null || _document.Chapters.Count == 0)
            return;

        var last = _document.Chapters.Count - 1;
        var start = _document.GetChapterStart(last);
        Paginate(last, start + _document.Chapters[last].Text.Length);
        Focus();
    }

    private void ChangeFontSize(double delta)
    {
        var value = Math.Clamp(Math.Round(_fontSize + delta), 12, 28);
        if (Math.Abs(value - _fontSize) < 0.01)
            return;

        _fontSize = value;
        FontSizeText.Text = ((int)_fontSize).ToString(CultureInfo.InvariantCulture);
        SaveSetting(FontSizeKey, _fontSize.ToString(CultureInfo.InvariantCulture));
        ApplyTypographyAndRepaginate();
    }

    // ── 事件 ──

    private void OnBackClick(object? sender, RoutedEventArgs e) => ShowLibrary();

    private void OnSettingsToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_document is null)
            return;

        _settingsOpen = !_settingsOpen;
        if (_settingsOpen)
            EnsureFontList();

        SettingsStrip.IsVisible = _settingsOpen;
        if (_settingsOpen)
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    private void OnCloseOnOutsideValueChanged(object? sender, ActionSwapValueChangedEventArgs e)
    {
        var closeOnOutside = string.Equals(e.Value, "on", StringComparison.OrdinalIgnoreCase);
        if (_suppressCloseOnOutside || closeOnOutside == _closeOnOutsideClick)
        {
            UpdateCloseOnOutsideTooltip();
            return;
        }

        _closeOnOutsideClick = closeOnOutside;
        SaveSetting(CloseOnOutsideClickKey, _closeOnOutsideClick ? "true" : "false");
        UpdateCloseOnOutsideTooltip();
    }

    private void UpdateCloseOnOutsideButton()
    {
        _suppressCloseOnOutside = true;
        CloseOnOutsideAction.Value = _closeOnOutsideClick ? "on" : "off";
        _suppressCloseOnOutside = false;
        UpdateCloseOnOutsideTooltip();
    }

    private void UpdateCloseOnOutsideTooltip()
    {
        ToolTip.SetTip(
            CloseOnOutsideAction,
            _closeOnOutsideClick
                ? L("CloseOnOutsideOn", "点外部关闭：开")
                : L("CloseOnOutsideOff", "点外部关闭：关"));
    }

    private void OnImageMenuValueChanged(object? sender, ActionSwapValueChangedEventArgs e)
    {
        if (_suppressImageMenu)
            return;

        var open = string.Equals(e.Value, "open", StringComparison.OrdinalIgnoreCase);
        if (open)
            _chromeHideTimer.Stop();
        ImageSettingsPopup.IsOpen = open;
    }

    private void OnImageSettingsPopupClosed(object? sender, EventArgs e)
    {
        _suppressImageMenu = true;
        ImageMenuAction.Value = "closed";
        _suppressImageMenu = false;
    }

    private void OnShowImagesValueChanged(object? sender, ActionSwapValueChangedEventArgs e)
    {
        var showImages = string.Equals(e.Value, "on", StringComparison.OrdinalIgnoreCase);
        if (_suppressImageSettings || showImages == _showImages)
            return;

        _showImages = showImages;
        SaveSetting(ShowImagesKey, _showImages ? "true" : "false");
        ApplyTypographyAndRepaginate();
    }

    private void OnGrayscaleImagesValueChanged(object? sender, ActionSwapValueChangedEventArgs e)
    {
        var grayscale = string.Equals(e.Value, "on", StringComparison.OrdinalIgnoreCase);
        if (_suppressImageSettings || grayscale == _grayscaleImages)
            return;

        _grayscaleImages = grayscale;
        SaveSetting(GrayscaleImagesKey, _grayscaleImages ? "true" : "false");
        if (_pages.Count > 0 && _pages[_pageIndex].Image is not null)
            RenderPage();
    }

    private void UpdateImageSettingsButtons()
    {
        _suppressImageSettings = true;
        try
        {
            ShowImagesAction.Value = _showImages ? "on" : "off";
            GrayscaleImagesAction.Value = _grayscaleImages ? "on" : "off";
        }
        finally
        {
            _suppressImageSettings = false;
        }

        ToolTip.SetTip(ImageMenuAction, L("ImageSettings", "图片设置"));
    }

    private void CloseImageSettingsMenu()
    {
        if (!ImageSettingsPopup.IsOpen)
            return;

        ImageSettingsPopup.IsOpen = false;
    }

    private void OnShelfModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressShelfMode)
            return;

        _shelfMode = ShelfModeControl.SelectedIndex == 1 ? 1 : 0;
        SaveSetting(ShelfModeKey, _shelfMode.ToString(CultureInfo.InvariantCulture));
        RebuildShelf(reloadItems: false);
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入电子书",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("电子书")
                    {
                        Patterns = EbookService.SupportedExtensions
                            .Select(extension => "*" + extension)
                            .ToArray(),
                    },
                },
            });

            var paths = files
                .Select(file => file.Path.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (paths.Length > 0)
                await ImportAsync(paths);
        }
        catch (Exception ex)
        {
            HeaderSubtitle.Text = ex.Message;
        }
    }

    private void OnPrevChapterClick(object? sender, RoutedEventArgs e) => GoToPreviousChapter();

    private void OnPrevPageClick(object? sender, RoutedEventArgs e) => GoToPreviousPage();

    private void OnNextPageClick(object? sender, RoutedEventArgs e) => GoToNextPage();

    private void OnNextChapterClick(object? sender, RoutedEventArgs e) => GoToNextChapter();

    private void OnFontSizeDecreaseClick(object? sender, RoutedEventArgs e) => ChangeFontSize(-1);

    private void OnFontSizeIncreaseClick(object? sender, RoutedEventArgs e) => ChangeFontSize(1);

    private void OnLeftZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            GoToPreviousPage();
            e.Handled = true;
        }
    }

    private void OnRightZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            GoToNextPage();
            e.Handled = true;
        }
    }

    private void OnPageFramePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible || ReaderListOverlay.IsVisible)
            return;

        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        var source = e.Source as Visual;
        if (IsWithin(source, LeftZone) ||
            IsWithin(source, RightZone) ||
            IsWithin(source, SelectionToolbar))
            return;

        if (SelectionToolbar.IsVisible)
        {
            HideSelectionToolbar();
            return;
        }

        var start = Math.Min(PageText.SelectionStart, PageText.SelectionEnd);
        var end = Math.Max(PageText.SelectionStart, PageText.SelectionEnd);
        if (end > start)
            return;

        e.Handled = true;
        ToggleReaderChrome();
    }

    private static bool IsWithin(Visual? source, Visual ancestor)
    {
        for (var current = source; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private void OnReaderWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_closeOnOutsideClick ||
            _suppressDeactivateClose ||
            _settingsOpen ||
            _document is null ||
            !ReaderPanel.IsVisible)
            return;

        CloseReaderPlugin();
    }

    private void CloseReaderPlugin()
    {
        if (_readerClosing)
            return;

        _readerClosing = true;
        SaveCurrentProgress();
        _service?.Flush();

        if (_readerWindow is { } window)
        {
            if (window.IsVisible)
                window.Close();
            return;
        }

        ShowLibrary(restoreFocus: false);
        _readerClosing = false;
    }

    private void OnReaderWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible)
            return;

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < 0.01)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            AdjustOpacity(delta);
            e.Handled = true;
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastWheelTurnUtc).TotalMilliseconds < 140)
        {
            e.Handled = true;
            return;
        }

        _wheelAccumulator += delta;
        if (Math.Abs(_wheelAccumulator) < 0.5)
        {
            e.Handled = true;
            return;
        }

        if (_wheelAccumulator > 0)
            GoToPreviousPage();
        else
            GoToNextPage();

        _wheelAccumulator = 0;
        _lastWheelTurnUtc = now;
        e.Handled = true;
    }

    private void OnFormChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_readerWindow is not { } window)
            return;

        if (!e.GetCurrentPoint(FormChrome).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Visual source && IsWithin(source, ReaderBackButton))
            return;

        window.BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnRootSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible || !_closeOnOutsideClick)
            return;

        if (!e.GetCurrentPoint(RootBorder).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(RootBorder);
        if (IsInside(PageFrame, point) ||
            IsInside(FormChrome, point) ||
            (ReaderStatusBar.IsVisible && IsInside(ReaderStatusBar, point)) ||
            (HeaderPanel.IsVisible && IsInside(HeaderPanel, point)) ||
            (ReaderFooter.IsVisible && IsInside(ReaderFooter, point)))
            return;

        e.Handled = true;
        CloseReaderPlugin();
    }

    private bool IsInside(Control control, Point point)
    {
        if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return false;

        var origin = control.TranslatePoint(new Point(0, 0), RootBorder);
        return origin is { } position && new Rect(position, control.Bounds.Size).Contains(point);
    }

    private void OnTopLevelWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_document is null || !ReaderPanel.IsVisible)
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        AdjustOpacity(e.Delta.Y);
        e.Handled = true;
    }

    private void AdjustOpacity(double delta)
    {
        if (Math.Abs(delta) < 0.01)
            return;

        _pageOpacity = Math.Clamp(
            _pageOpacity + (delta > 0 ? 0.08 : -0.08),
            0.1,
            1.0);
        SaveSettingDebounced(PageOpacityKey, _pageOpacity.ToString(CultureInfo.InvariantCulture));
        ApplyTheme();
    }

    private void OnPageContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_paginating || _document is null || !ReaderPanel.IsVisible)
            return;

        if (e.NewSize.Width < 40 || e.NewSize.Height < 40)
            return;

        if (Math.Abs(e.NewSize.Width - _lastTextWidth) < 1 &&
            Math.Abs(e.NewSize.Height - _lastTextHeight) < 1)
            return;

        _lastTextWidth = e.NewSize.Width;
        _lastTextHeight = e.NewSize.Height;
        Paginate(_chapterIndex, CurrentGlobalOffset());
    }

    private void OnFontFamilyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
            return;

        var name = FontSelect.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(name))
            return;

        _fontFamilyName = name == L("SystemDefault", "系统默认") ? null : name;

        SaveSetting(FontFamilyKey, _fontFamilyName ?? "");
        ApplyTypographyAndRepaginate();
    }

    private void OnLineSpacingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
            return;

        var value = LineSpacingSelect.SelectedValue as string;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing))
            return;

        _lineSpacing = Math.Clamp(spacing, 1.4, 2.0);
        SaveSetting(LineSpacingKey, _lineSpacing.ToString(CultureInfo.InvariantCulture));
        ApplyTypographyAndRepaginate();
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
            return;

        var value = ThemeSelect.SelectedValue;
        if (value is null)
            return;

        var index = value switch
        {
            int intValue => intValue,
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => 0,
        };

        _themeIndex = Math.Clamp(index, 0, 2);
        SaveSetting(ThemeKey, _themeIndex.ToString(CultureInfo.InvariantCulture));
        ApplyTheme();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (_settingsOpen)
        {
            if (e.Key == Key.Escape)
            {
                _settingsOpen = false;
                SettingsStrip.IsVisible = false;
                e.Handled = true;
                Focus();
            }

            return;
        }

        if (!ReaderPanel.IsVisible || _document is null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (e.Key == Key.Left)
            {
                GoToPreviousChapter();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                GoToNextChapter();
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.PageUp:
            case Key.Up:
                GoToPreviousPage();
                e.Handled = true;
                break;

            case Key.Right:
            case Key.PageDown:
            case Key.Down:
            case Key.Space:
                GoToNextPage();
                e.Handled = true;
                break;

            case Key.Home:
                GoToBookStart();
                e.Handled = true;
                break;

            case Key.End:
                GoToBookEnd();
                e.Handled = true;
                break;

            case Key.OemPlus:
            case Key.Add:
                ChangeFontSize(1);
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                ChangeFontSize(-1);
                e.Handled = true;
                break;
        }
    }

}

internal enum ReaderListMode
{
    None,
    Toc,
    Bookmarks,
    Notes,
}
