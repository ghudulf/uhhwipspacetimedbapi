using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class RouteSchedulesManagementToolWindow : UserControl
    {
        public RouteSchedulesManagementToolWindow()
        {
            InitializeComponent();
            DataContext = new RouteSchedulesManagementViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
} 