using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Converters;

public class BoolToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly SolidColorBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#F48771"));
    private static readonly SolidColorBrush UnknownBrush = new SolidColorBrush(Colors.Gray);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? ConnectedBrush : DisconnectedBrush;
        }
        return UnknownBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToConnectTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? "🔌 Disconnect" : "🔌 Connect";
        }
        return "🔌 Connect";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StatusToClassConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TestStatus status)
        {
            return status.ToString();
        }
        return "NotTested";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StatusToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush NotTestedBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly SolidColorBrush TestingBrush = new SolidColorBrush(Color.Parse("#2D4F67"));
    private static readonly SolidColorBrush PassedBrush = new SolidColorBrush(Color.Parse("#1E4620"));
    private static readonly SolidColorBrush FailedBrush = new SolidColorBrush(Color.Parse("#5A1E1E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TestStatus status)
        {
            return status switch
            {
                TestStatus.NotTested => NotTestedBrush,
                TestStatus.Testing => TestingBrush,
                TestStatus.Passed => PassedBrush,
                TestStatus.Failed => FailedBrush,
                _ => NotTestedBrush
            };
        }
        return NotTestedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StatusToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NotTestedBrush = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly SolidColorBrush TestingBrush = new SolidColorBrush(Color.Parse("#3A7CA5"));
    private static readonly SolidColorBrush PassedBrush = new SolidColorBrush(Color.Parse("#2D7A2E"));
    private static readonly SolidColorBrush FailedBrush = new SolidColorBrush(Color.Parse("#8B2E2E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TestStatus status)
        {
            return status switch
            {
                TestStatus.NotTested => NotTestedBrush,
                TestStatus.Testing => TestingBrush,
                TestStatus.Passed => PassedBrush,
                TestStatus.Failed => FailedBrush,
                _ => NotTestedBrush
            };
        }
        return NotTestedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
