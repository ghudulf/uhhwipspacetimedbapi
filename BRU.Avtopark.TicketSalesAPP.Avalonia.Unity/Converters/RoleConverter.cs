using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Converters
{
    public class RoleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int role)
            {
                return role == 1 ? "Admin" : "User";
            }
            return "Unknown";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string roleStr)
            {
                return roleStr == "Admin" ? 1 : 0;
            }
            return 0;
        }
    }
} 