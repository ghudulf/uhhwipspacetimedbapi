using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class LoginMethodSelectorWindow : Window
    {
        public LoginMethod? SelectedMethod { get; private set; }

        public LoginMethodSelectorWindow()
        {
            InitializeComponent();
            this.FindControl<Button>("WindowsAccountButton")!.IsVisible =
                Services.WindowsAccountClient.TrustedOrigin != null;
#if DEBUG
            DevToolsHelper.AttachOnce();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void TraditionalLogin_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = LoginMethod.Traditional;
            Close(SelectedMethod);
        }

        private void OAuthLogin_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = LoginMethod.OAuth;
            Close(SelectedMethod);
        }

        private void WindowsAccount_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = LoginMethod.WindowsAccount;
            Close(SelectedMethod);
        }

        private void QrCode_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = LoginMethod.QrCode;
            Close(SelectedMethod);
        }

        private void QrDevice_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = LoginMethod.QrDevice;
            Close(SelectedMethod);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            SelectedMethod = null;
            Close(SelectedMethod);
        }
    }

    public enum LoginMethod
    {
        WindowsAccount,
        Traditional,
        OAuth,
        QrCode,
        QrDevice
    }
}
