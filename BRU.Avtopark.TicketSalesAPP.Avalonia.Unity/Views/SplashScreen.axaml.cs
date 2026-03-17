using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class SplashScreen : Window
    {
        private const string AdminServerUrl = "http://localhost:5000";

        private TextBlock? _statusMessage;
        private Border? _errorPanel;
        private TextBlock? _errorMessage;
        private Ellipse?[] _dots = Array.Empty<Ellipse?>();
        private DispatcherTimer? _dotTimer;
        private int _dotIndex = 0;

        public SplashScreen()
        {
            InitializeComponent();
            _statusMessage = this.FindControl<TextBlock>("StatusMessage");
            _errorPanel    = this.FindControl<Border>("ErrorPanel");
            _errorMessage  = this.FindControl<TextBlock>("ErrorMessage");
            _dots = new[]
            {
                this.FindControl<Ellipse>("Dot1"),
                this.FindControl<Ellipse>("Dot2"),
                this.FindControl<Ellipse>("Dot3"),
                this.FindControl<Ellipse>("Dot4"),
            };

            StartDotAnimation();
        }

        private void StartDotAnimation()
        {
            _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _dotTimer.Tick += (_, _) =>
            {
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] is Ellipse dot)
                        dot.Opacity = i == _dotIndex ? 1.0 : 0.2;
                }
                _dotIndex = (_dotIndex + 1) % _dots.Length;
            };
            _dotTimer.Start();
        }

        public void StopDotAnimation() => _dotTimer?.Stop();

        public void SetStatus(string text)
        {
            if (_statusMessage != null)
                _statusMessage.Text = text;
        }

        public async Task<bool> CheckServerAvailability()
        {
            SetStatus("Проверка подключения...");
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync($"{AdminServerUrl}/swagger");
                if (response.IsSuccessStatusCode)
                {
                    SetStatus("Подключено");
                    if (_errorPanel != null) _errorPanel.IsVisible = false;
                    return true;
                }
            }
            catch { }

            SetStatus("Ошибка подключения");
            if (_errorPanel != null)  _errorPanel.IsVisible = true;
            if (_errorMessage != null) _errorMessage.Text =
                "Сервер недоступен. Убедитесь, что TicketSalesApp.AdminServer запущен.";
            return false;
        }
    }
}
