using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class ModalDialog : Window
    {
        private ModalDialogViewModel _viewModel;

        public string Title
        {
            get => _viewModel?.Title ?? string.Empty;
            set
            {
                if (_viewModel != null)
                    _viewModel.Title = value;
            }
        }

        public string Message
        {
            get => _viewModel?.Message ?? string.Empty;
            set
            {
                if (_viewModel != null)
                    _viewModel.Message = value;
            }
        }

        public ModalDialogType DialogType
        {
            get => _viewModel?.DialogType ?? ModalDialogType.Information;
            set
            {
                if (_viewModel != null)
                    _viewModel.DialogType = value;
            }
        }

        public ModalDialog()
        {
            _viewModel = new ModalDialogViewModel(); // Initialize ViewModel first
            DataContext = _viewModel;
            InitializeComponent();

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public async Task<bool?> ShowDialog(Window owner)
        {
            if (owner != null)
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            return await ShowDialog<bool?>(owner);
        }
    }
}