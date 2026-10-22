using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class EmployeeManagementToolWindow : UserControl
    {
        public EmployeeManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new EmployeeEditorService(dialogs);
            DataContext = new EmployeeManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление сотрудника", $"Удалить сотрудника «{row.Surname} {row.Name}» (ID {row.EmployeeId})? Связанные записи могут запретить удаление.", ct));
            AttachedToVisualTree += async (_, _) => { if (DataContext is EmployeeManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is EmployeeManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
