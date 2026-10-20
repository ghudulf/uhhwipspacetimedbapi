using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Converters;

public sealed class UnixMillisecondsConverter : IValueConverter
{
    public static UnixMillisecondsConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ulong timestamp || timestamp > 253402300799999UL) return "—";
        return DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm", culture);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ZeroCountConverter : IValueConverter
{
    public static ZeroCountConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int count && count == 0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
