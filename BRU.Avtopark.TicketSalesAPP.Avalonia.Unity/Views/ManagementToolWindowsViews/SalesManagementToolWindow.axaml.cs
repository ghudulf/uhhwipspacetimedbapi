using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class SalesManagementToolWindow : UserControl
    {
        public SalesManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new SaleEditorService(dialogs);
            DataContext = new SalesManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление продажи", $"Удалить продажу {row.SaleId} (билет {row.TicketId})?", ct));
            AttachedToVisualTree += async (_, _) => { if (DataContext is SalesManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is SalesManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
