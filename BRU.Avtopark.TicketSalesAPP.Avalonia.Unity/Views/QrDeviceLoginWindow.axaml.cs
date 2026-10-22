using Avalonia.Controls;
using Avalonia.Interactivity;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class QrDeviceLoginWindow : Window
{
    public string? LoginJson { get; private set; }
    public QrDeviceLoginWindow()
    {
        InitializeComponent();
        var session = new QrWindowSession(this);
        var vm = new QrDeviceLoginViewModel(QrClientContext.CreateApi(), DeviceKeyStore.Current);
        DataContext = vm; session.Bind(vm);
        vm.Completed += json => { LoginJson = json; Close(); };
    }
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();
}
