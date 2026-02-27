using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class SplashScreen : Window
    {
        private readonly TextBlock? _statusMessage;
        private readonly TextBlock? _errorMessage;
        private const string AdminServerUrl = "http://localhost:5000"; // Update this to match your server port

        public SplashScreen()
        {
            InitializeComponent();
            _statusMessage = this.FindControl<TextBlock>("StatusMessage");
            _errorMessage = this.FindControl<TextBlock>("ErrorMessage");
        }

        public async Task<bool> CheckServerAvailability()
        {
            if (_statusMessage != null)
                _statusMessage.Text = "Проверка подключения к серверу...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync($"{AdminServerUrl}/swagger");

                if (response.IsSuccessStatusCode)
                {
                    if (_statusMessage != null)
                        _statusMessage.Text = "Подключение к серверу установлено";
                    if (_errorMessage != null)
                        _errorMessage.IsVisible = false;
                    return true;
                }
            }
            catch (Exception)
            {
                // Server is not available
            }

            if (_statusMessage != null)
                _statusMessage.Text = "Ошибка подключения к серверу";
            if (_errorMessage != null)
                _errorMessage.IsVisible = true;

            return false;
        }
    }
} 