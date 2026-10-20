using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class WindowsAccountLinkWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _applicationToken;

    public WindowsAccountLinkWindow()
    {
        InitializeComponent();
        _applicationToken = ApiClientService.Instance.AuthToken;
        ApiClientService.Instance.OnAuthTokenChanged += SessionChanged;
        Closed += (_, _) =>
        {
            ApiClientService.Instance.OnAuthTokenChanged -= SessionChanged;
            _lifetime.Cancel();
        };
    }

    private void SessionChanged(object? sender, string? token)
    {
        if (token != _applicationToken)
        {
            _lifetime.Cancel();
            Dispatcher.UIThread.Post(Close);
        }
    }

    private async void Link_Click(object? sender, RoutedEventArgs args)
    {
        LinkButton.IsEnabled = false;
        StatusButton.IsEnabled = false;
        StatusText.Text = "Проверка учётной записи домена…";
        try
        {
            await WindowsAccountClient.LinkAsync(_applicationToken ?? "", _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) StatusText.Text = "Учётная запись связана. Windows-вход доступен при следующем входе.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Запрос отменён. Если он был отправлен, проверьте результат перед повтором."; }
        catch (InvalidOperationException ex) { StatusText.Text = ex.Message; }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Account linking request failed: {ErrorType}", ex.GetType().Name);
            StatusText.Text = "Не удалось подтвердить результат. Проверьте связь с сервером перед повтором.";
        }
        if (!_lifetime.IsCancellationRequested) StatusButton.IsEnabled = true;
        // A link is never automatically retried after an uncertain response.
    }

    private async void CheckStatus_Click(object? sender, RoutedEventArgs args)
    {
        StatusButton.IsEnabled = false;
        LinkButton.IsEnabled = false;
        try
        {
            var response = await WindowsAccountClient.GetLinkStatusAsync(_applicationToken ?? "", _lifetime.Token);
            using var document = System.Text.Json.JsonDocument.Parse(response);
            if (_lifetime.IsCancellationRequested) return;
            var state = document.RootElement.GetProperty("state").GetString();
            StatusText.Text = state switch
            {
                "linked" => "Учётная запись Windows связана с текущей учётной записью приложения.",
                "unlinked" => "Учётная запись Windows ещё не связана. Можно создать связь.",
                "linked_elsewhere" => "Учётная запись Windows уже связана с другой учётной записью. Обратитесь к администратору.",
                _ => "Сервер вернул неизвестное состояние связи."
            };
            LinkButton.IsEnabled = state == "unlinked";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Account link status failed: {ErrorType}", ex.GetType().Name);
            if (!_lifetime.IsCancellationRequested) StatusText.Text = "Не удалось проверить связь. Проверьте подключение и повторите проверку.";
        }
        finally { if (!_lifetime.IsCancellationRequested) StatusButton.IsEnabled = true; }
    }

    private void Close_Click(object? sender, RoutedEventArgs args) => Close();
}
