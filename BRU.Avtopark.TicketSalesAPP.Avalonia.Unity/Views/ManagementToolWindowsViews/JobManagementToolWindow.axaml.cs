using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class JobManagementToolWindow : UserControl
    {
        public JobManagementToolWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}