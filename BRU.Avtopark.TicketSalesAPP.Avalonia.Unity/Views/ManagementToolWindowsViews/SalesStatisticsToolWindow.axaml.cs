using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class SalesStatisticsToolWindow : UserControl
    {
        public SalesStatisticsToolWindow()
        {
            InitializeComponent();
            DataContext = new SalesStatisticsViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}