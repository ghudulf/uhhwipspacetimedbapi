using System.Windows.Input;
using ReactiveUI;
using System;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public class ModalDialogViewModel : ReactiveObject
    {
        private string _title;
        private object _content;
        private string _message;
        private ModalDialogType _dialogType;
        private string _primaryButtonText;
        private string _secondaryButtonText;
        private string _closeButtonText;
        private bool _showPrimaryButton;
        private bool _showSecondaryButton;
        private bool _showCloseButton;
        private ICommand _primaryButtonCommand;
        private ICommand _secondaryButtonCommand;
        private ICommand _closeButtonCommand;

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public object Content
        {
            get => _content;
            set => this.RaiseAndSetIfChanged(ref _content, value);
        }

        public string Message
        {
            get => _message;
            set => this.RaiseAndSetIfChanged(ref _message, value);
        }

        public ModalDialogType DialogType
        {
            get => _dialogType;
            set => this.RaiseAndSetIfChanged(ref _dialogType, value);
        }

        public string PrimaryButtonText
        {
            get => _primaryButtonText;
            set => this.RaiseAndSetIfChanged(ref _primaryButtonText, value);
        }

        public string SecondaryButtonText
        {
            get => _secondaryButtonText;
            set => this.RaiseAndSetIfChanged(ref _secondaryButtonText, value);
        }

        public string CloseButtonText
        {
            get => _closeButtonText;
            set => this.RaiseAndSetIfChanged(ref _closeButtonText, value);
        }

        public bool ShowPrimaryButton
        {
            get => _showPrimaryButton;
            set => this.RaiseAndSetIfChanged(ref _showPrimaryButton, value);
        }

        public bool ShowSecondaryButton
        {
            get => _showSecondaryButton;
            set => this.RaiseAndSetIfChanged(ref _showSecondaryButton, value);
        }

        public bool ShowCloseButton
        {
            get => _showCloseButton;
            set => this.RaiseAndSetIfChanged(ref _showCloseButton, value);
        }

        public ICommand PrimaryButtonCommand
        {
            get => _primaryButtonCommand;
            set => this.RaiseAndSetIfChanged(ref _primaryButtonCommand, value);
        }

        public ICommand SecondaryButtonCommand
        {
            get => _secondaryButtonCommand;
            set => this.RaiseAndSetIfChanged(ref _secondaryButtonCommand, value);
        }

        public ICommand CloseButtonCommand
        {
            get => _closeButtonCommand;
            set => this.RaiseAndSetIfChanged(ref _closeButtonCommand, value);
        }

        public ModalDialogViewModel()
        {
            ShowCloseButton = true;
            CloseButtonText = "Close";
            Title = "PlaceHoldr";
            DialogType = ModalDialogType.Information;
        }
    }
}