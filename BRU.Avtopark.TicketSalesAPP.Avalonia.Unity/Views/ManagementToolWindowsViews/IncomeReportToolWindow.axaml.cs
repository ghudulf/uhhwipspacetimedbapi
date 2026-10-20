using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class IncomeReportToolWindow : UserControl
    {
        public IncomeReportToolWindow()
        {
            InitializeComponent();
            var viewModel = new IncomeReportViewModel();
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