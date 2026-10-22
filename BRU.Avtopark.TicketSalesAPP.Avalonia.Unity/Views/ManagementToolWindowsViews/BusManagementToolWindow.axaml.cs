using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class BusManagementToolWindow : UserControl
    {
        public BusManagementToolWindow()
        {
            InitializeComponent();
            DataContext = new BusManagementViewModel();
            AttachedToVisualTree += async (_, _) => { if (DataContext is BusManagementViewModel viewModel) await viewModel.ActivateAsync(); };
            DetachedFromVisualTree += (_, _) => { if (DataContext is BusManagementViewModel viewModel) viewModel.Deactivate(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}