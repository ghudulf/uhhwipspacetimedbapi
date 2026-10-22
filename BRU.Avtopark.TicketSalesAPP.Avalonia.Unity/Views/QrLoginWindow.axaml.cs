using Avalonia.Controls;
using Avalonia.Interactivity;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class QrLoginWindow : Window
{
    public string? LoginJson { get; private set; }
    public QrLoginWindow()
    {
        InitializeComponent();
        var session = new QrWindowSession(this);
        var vm = new QrLoginViewModel(QrClientContext.CreateApi());
        DataContext = vm; session.Bind(vm);
        vm.Completed += json => { LoginJson = json; Close(); };
        Opened += async (_, _) => await vm.StartCommand.ExecuteAsync(null);
    }
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();
}
