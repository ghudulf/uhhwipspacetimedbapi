using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class SalesManagementToolWindow : UserControl
    {
        public SalesManagementToolWindow()
        {
            InitializeComponent();
            this.FindControl<DataGrid>("SalesGrid")!.AddHandler(InputElement.KeyDownEvent, OnSaleKeyDown, RoutingStrategies.Tunnel);
            var dialogs = new ControlDialogHost(this);
            var editor = new SaleEditorService(dialogs);
            DataContext = new SalesManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление продажи", $"Удалить продажу {row.SaleId} (билет {row.TicketId})?", ct));
            AttachedToVisualTree += async (_, _) => { if (DataContext is SalesManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is SalesManagementViewModel model) model.Deactivate(); };
        }

        private async void OnSaleKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || DataContext is not SalesManagementViewModel model || !model.EditCommand.CanExecute(null)) return;
            e.Handled = true;
            await model.EditCommand.ExecuteAsync(null);
        }

        private async void OnSaleDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SalesManagementViewModel model || !model.EditCommand.CanExecute(null)) return;
            e.Handled = true;
            await model.EditCommand.ExecuteAsync(null);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
