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
#if DEBUG
            DevToolsHelper.AttachOnce();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void TraditionalLogin_Click(object? sender, PointerPressedEventArgs e)
        {
            SelectedMethod = LoginMethod.Traditional;
            Close(SelectedMethod);
        }

        private void OAuthLogin_Click(object? sender, PointerPressedEventArgs e)
        {
            SelectedMethod = LoginMethod.OAuth;
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
        Traditional,
        OAuth
    }
}
