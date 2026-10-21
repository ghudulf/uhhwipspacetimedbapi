using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class RouteManagementToolWindow : UserControl
    {
        public RouteManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new RouteEditorService(dialogs);
            var viewModel = new RouteManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление маршрута", $"Удалить маршрут «{row.RouteNumber} {row.StartPoint} — {row.EndPoint}» (ID {row.RouteId})? Билеты и расписания могут запретить удаление.", ct));
            DataContext = viewModel;
            AttachedToVisualTree += async (_, _) => { if (DataContext is RouteManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is RouteManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
