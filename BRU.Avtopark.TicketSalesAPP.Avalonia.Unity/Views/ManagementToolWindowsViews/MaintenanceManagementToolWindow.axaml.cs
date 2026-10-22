using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class MaintenanceManagementToolWindow : UserControl
    {
        public MaintenanceManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new MaintenanceEditorService(dialogs);
            DataContext = new MaintenanceManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление записи обслуживания", $"Удалить запись обслуживания ID {row.MaintenanceId} автобуса «{row.BusModel}»? Связанные записи могут запретить удаление.", ct));
            AttachedToVisualTree += async (_, _) => { if (DataContext is MaintenanceManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is MaintenanceManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
