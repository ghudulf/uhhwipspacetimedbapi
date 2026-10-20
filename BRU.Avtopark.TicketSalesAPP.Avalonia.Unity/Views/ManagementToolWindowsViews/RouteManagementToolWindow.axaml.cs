using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class RouteManagementToolWindow : UserControl
    {
        public RouteManagementToolWindow()
        {
            InitializeComponent();
            var viewModel = new RouteManagementViewModel();
            DataContext = viewModel;
            AttachedToVisualTree += async (_, _) => await viewModel.ActivateAsync();
            DetachedFromVisualTree += (_, _) => viewModel.Deactivate();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}