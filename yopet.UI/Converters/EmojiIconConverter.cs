using System.Globalization;
using Avalonia.Data.Converters;
using TKCode.Arc.Icons.Lucide;
using yopet.UI.Components;

namespace yopet.UI.Converters;

public sealed class EmojiIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        YopetIcons.Resolve(value as string, LucideIconKind.Sparkles);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
