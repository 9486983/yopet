using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.DesignTokens;

namespace yopet.UI.Components;

/// <summary>Arc 对话框表面：标题、说明、内容和右侧操作区。</summary>
public class ArcDialogCard : ArcSurface
{
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly ContentControl _body;
    private readonly ContentControl _actions;
    private string? _descriptionText;

    public ArcDialogCard()
    {
        _title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        ArcTokens.Bind(_title, TextBlock.ForegroundProperty, ArcDesignTokenKind.Foreground);
        ArcTokens.Bind(_title, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeLG);

        _description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
        };
        ArcTokens.Bind(_description, TextBlock.ForegroundProperty, ArcDesignTokenKind.TextSecondary);
        ArcTokens.Bind(_description, TextBlock.FontSizeProperty, ArcDesignTokenKind.TypeSM);

        _body = new ContentControl { Margin = new Thickness(0, 16, 0, 0) };
        _actions = new ContentControl
        {
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = false,
        };

        Child = new StackPanel
        {
            Children =
            {
                _title,
                _description,
                _body,
                _actions,
            },
        };
    }

    public string? Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public string? Description
    {
        get => _descriptionText;
        set
        {
            _descriptionText = value;
            _description.Text = value;
            _description.IsVisible = !string.IsNullOrWhiteSpace(value);
        }
    }

    public Control? Body
    {
        get => _body.Content as Control;
        set => _body.Content = value;
    }

    public Control? Actions
    {
        get => _actions.Content as Control;
        set
        {
            _actions.Content = value;
            _actions.IsVisible = value is not null;
        }
    }
}
