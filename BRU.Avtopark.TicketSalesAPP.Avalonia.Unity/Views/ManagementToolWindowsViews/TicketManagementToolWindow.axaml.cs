using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class TicketManagementToolWindow : UserControl
    {
        public TicketManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this);
            var editor = new TicketEditorService(dialogs);
            DataContext = new TicketManagementViewModel(null, edit: editor.ShowAsync,
                confirmDelete: (row, ct) => dialogs.ConfirmAsync("Удаление билета", $"Удалить билет ID {row.Id} маршрута «{row.RouteLabel}»? Продажа, ссылающаяся на билет, запрещает удаление.", ct));
            AttachedToVisualTree += async (_, _) => { if (DataContext is TicketManagementViewModel model) await model.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is TicketManagementViewModel model) model.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
