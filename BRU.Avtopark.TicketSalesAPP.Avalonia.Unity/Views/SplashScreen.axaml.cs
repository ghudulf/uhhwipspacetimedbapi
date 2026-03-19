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
        private EventHandler? _dotTickHandler;
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
            Closed += (_, _) => StopDotAnimation();
        }

        private void StartDotAnimation()
        {
            _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _dotTickHandler = (_, _) =>
            {
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] is Ellipse dot)
                        dot.Opacity = i == _dotIndex ? 1.0 : 0.2;
                }
                _dotIndex = (_dotIndex + 1) % _dots.Length;
            };
            _dotTimer.Tick += _dotTickHandler;
            _dotTimer.Start();
        }

        public void StopDotAnimation()
        {
            _dotTimer?.Stop();
            if (_dotTimer != null && _dotTickHandler != null)
                _dotTimer.Tick -= _dotTickHandler;
            _dotTimer = null;
            _dotTickHandler = null;
        }

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

                // Try health endpoint first, fall back to swagger
                foreach (var path in new[] { "/health", "/healthz", "/swagger" })
                {
                    try
                    {
                        var response = await client.GetAsync($"{AdminServerUrl}{path}");
                        if (response.IsSuccessStatusCode)
                        {
                            SetStatus("Подключено");
                            if (_errorPanel != null) _errorPanel.IsVisible = false;
                            return true;
                        }
                        // Non-success but server responded — show status code
                        SetStatus($"Ошибка подключения (HTTP {(int)response.StatusCode})");
                        if (_errorPanel != null) _errorPanel.IsVisible = true;
                        if (_errorMessage != null) _errorMessage.Text =
                            $"Сервер вернул HTTP {(int)response.StatusCode}. Убедитесь, что TicketSalesApp.AdminServer запущен.";
                        return false;
                    }
                    catch (TaskCanceledException)
                    {
                        // Timeout on this path — try next
                    }
                    catch (HttpRequestException)
                    {
                        // Connection refused on this path — try next
                    }
                }
            }
            catch (TaskCanceledException)
            {
                SetStatus("Ошибка подключения (таймаут)");
                if (_errorPanel != null) _errorPanel.IsVisible = true;
                if (_errorMessage != null) _errorMessage.Text =
                    "Превышено время ожидания. Убедитесь, что TicketSalesApp.AdminServer запущен.";
                return false;
            }
            catch (HttpRequestException ex)
            {
                SetStatus("Ошибка подключения (отказ)");
                if (_errorPanel != null) _errorPanel.IsVisible = true;
                if (_errorMessage != null) _errorMessage.Text =
                    $"Соединение отклонено: {ex.Message}. Убедитесь, что TicketSalesApp.AdminServer запущен.";
                return false;
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка подключения");
                if (_errorPanel != null) _errorPanel.IsVisible = true;
                if (_errorMessage != null) _errorMessage.Text =
                    $"Сервер недоступен: {ex.Message}";
                return false;
            }

            SetStatus("Ошибка подключения");
            if (_errorPanel != null) _errorPanel.IsVisible = true;
            if (_errorMessage != null) _errorMessage.Text =
                "Сервер недоступен. Убедитесь, что TicketSalesApp.AdminServer запущен.";
            return false;
        }
    }
}
