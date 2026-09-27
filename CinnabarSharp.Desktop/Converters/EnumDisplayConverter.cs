using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace CinnabarSharp.Desktop.Converters;

/// <summary>Shows enum values as words: "BestQuality" → "Best Quality".</summary>
public sealed partial class EnumDisplayConverter : IValueConverter
{
    public static EnumDisplayConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? WordBoundary().Replace(e.ToString(), " ") : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
