using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class JobManagementToolWindow : UserControl
    {
        public JobManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new JobEditorService(dialogs);
            DataContext = new JobManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: row => dialogs.ConfirmAsync("Удаление должности", $"Удалить должность «{row.JobTitle}» (ID {row.JobId})? Связанные записи могут запретить удаление."));
            AttachedToVisualTree += async (_, _) => { if (DataContext is JobManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is JobManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}