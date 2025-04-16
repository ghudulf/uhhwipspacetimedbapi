using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Converters
{
    public class ResponsiveColumnsConverter : IValueConverter
    {
        private static ResponsiveColumnsConverter? _instance;
        public static ResponsiveColumnsConverter Instance => _instance ??= new ResponsiveColumnsConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // Determine number of columns based on window width
                if (width < 800) return 1;
                if (width < 1200) return 2;
                return 3;
            }
            return 2; // Default to 2 columns
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}