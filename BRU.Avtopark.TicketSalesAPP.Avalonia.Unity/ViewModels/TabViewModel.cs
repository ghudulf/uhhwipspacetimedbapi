using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Material.Icons;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public class TabBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSelected = (bool)value;
        return isSelected ? 
            new SolidColorBrush(Color.Parse("#3C3F41")) : 
            new SolidColorBrush(Color.Parse("#2B2D30"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
    public class TabViewModel : ReactiveObject
    {
        private string _title;
        private MaterialIconKind _icon;
        private bool _isSelected;
        private Control _content;
        private Window _sourceWindow;

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public MaterialIconKind Icon
        {
            get => _icon;
            set => this.RaiseAndSetIfChanged(ref _icon, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public Control Content
        {
            get => _content;
            set => this.RaiseAndSetIfChanged(ref _content, value);
        }

        public Window SourceWindow
        {
            get => _sourceWindow;
            set => this.RaiseAndSetIfChanged(ref _sourceWindow, value);
        }
    }
}