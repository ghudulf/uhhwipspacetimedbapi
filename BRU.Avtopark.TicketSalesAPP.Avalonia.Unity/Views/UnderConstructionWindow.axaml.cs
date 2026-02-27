using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class UnderConstructionWindow : Window
{
    public UnderConstructionWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOKButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}