using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace yopet.UI.Converters;

/// <summary>Scales a measured length for a scrollbar track (keeps it inside rounded corners).</summary>
public sealed class ScaleConverter : IValueConverter
{
    public double Ratio { get; set; } = 0.9;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double length && double.IsFinite(length))
            return length * Ratio;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
