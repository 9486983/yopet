using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;
using yopet.Sdk;

namespace yopet.UI.Components;

/// <summary>A single-row theme selector with six color previews.</summary>
public sealed class ThemePicker : UserControl
{
    private readonly IReadOnlyList<ArcUi.ThemeChoice> _choices;
    private readonly Action<ArcUi.ThemeChoice> _onSelected;
    private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _labels = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _title;
    private string _selectedId;

    public ThemePicker(
        IReadOnlyList<ArcUi.ThemeChoice> choices,
        string? selectedId,
        Action<ArcUi.ThemeChoice> onSelected)
    {
        _choices = choices;
        _onSelected = onSelected;
        _selectedId = selectedId ?? choices.FirstOrDefault()?.Id ?? "";

        _title = new TextBlock
        {
            Text = Label("Localization.Theme.Label", "Theme"),
            FontWeight = FontWeight.SemiBold,
        };
        ArcTokens.Bind(_title, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);
        ArcTokens.Bind(_title, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeSM);

        var grid = new UniformGrid
        {
            Columns = Math.Max(1, choices.Count),
            ColumnSpacing = 8,
        };

        foreach (var choice in choices)
        {
            var preview = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(choice.PreviewSurface),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#22000000")),
                Child = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(choice.PreviewAccent),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0),
                },
            };

            var label = new TextBlock
            {
                Text = Label(choice.LabelKey, choice.Id),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ArcTokens.Bind(label, TextBlock.ForegroundProperty, ArcDesignTokenKind.TextSecondary);
            ArcTokens.Bind(label, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeXS);

            var card = new Border
            {
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children = { preview, label },
                },
            };
            ArcTokens.Bind(card, Border.BackgroundProperty, ArcDesignTokenKind.SurfaceMuted);
            ArcTokens.Bind(card, Border.BorderBrushProperty, ArcDesignTokenKind.BorderSubtle);
            card.PointerPressed += (_, _) => Select(choice);

            _cards[choice.Id] = card;
            _labels[choice.Id] = label;
            grid.Children.Add(card);
        }

        Content = new StackPanel
        {
            Spacing = 8,
            Children = { _title, grid },
        };

        UpdateSelection();
    }

    public void Select(string id)
    {
        var choice = _choices.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (choice is not null) Select(choice);
    }

    public void RefreshLabels()
    {
        _title.Text = Label("Localization.Theme.Label", "Theme");
        foreach (var choice in _choices)
        {
            if (_labels.TryGetValue(choice.Id, out var label))
                label.Text = Label(choice.LabelKey, choice.Id);
        }
    }

    private void Select(ArcUi.ThemeChoice choice)
    {
        _selectedId = choice.Id;
        UpdateSelection();
        _onSelected(choice);
    }

    private void UpdateSelection()
    {
        foreach (var choice in _choices)
        {
            if (!_cards.TryGetValue(choice.Id, out var card)) continue;
            var selected = string.Equals(choice.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
            if (selected)
            {
                card.BorderBrush = new SolidColorBrush(choice.PreviewAccent);
                card.BorderThickness = new Thickness(2);
            }
            else
            {
                card.BorderBrush = new SolidColorBrush(Color.Parse("#22000000"));
                card.BorderThickness = new Thickness(2);
            }
        }
    }

    private static string Label(string key, string fallback)
    {
        try
        {
            var text = Localization.T(key);
            return string.IsNullOrWhiteSpace(text) || text == key ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }
}
