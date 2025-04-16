using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class TicketManagementToolWindow : UserControl
    {
        public TicketManagementToolWindow()
        {
            InitializeComponent();
            DataContext = new TicketManagementViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}