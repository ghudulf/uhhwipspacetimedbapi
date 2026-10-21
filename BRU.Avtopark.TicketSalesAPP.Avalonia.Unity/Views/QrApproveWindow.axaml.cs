using Avalonia.Controls;
using Avalonia.Interactivity;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class QrApproveWindow : Window
{
    public QrApproveWindow()
    {
        InitializeComponent();
        var session = new QrWindowSession(this);
        var api = QrClientContext.CreateApi();
        var vm = new QrApproveViewModel(api, DeviceKeyStore.Current, session.Bearer, "", ct => QrClientContext.AccountAsync(api.ApiBase, session.Bearer, ct));
        DataContext = vm; session.Bind(vm);
    }
    private void Close_Click(object? sender, RoutedEventArgs args) => Close();
}
