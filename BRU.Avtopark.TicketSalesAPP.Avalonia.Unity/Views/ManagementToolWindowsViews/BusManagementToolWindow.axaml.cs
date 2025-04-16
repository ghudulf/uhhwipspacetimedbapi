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
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}