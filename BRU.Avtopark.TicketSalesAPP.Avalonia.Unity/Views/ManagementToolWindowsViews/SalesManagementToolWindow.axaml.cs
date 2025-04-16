using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class SalesManagementToolWindow : UserControl
    {
        public SalesManagementToolWindow()
        {
            InitializeComponent();
            DataContext = new SalesManagementViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}