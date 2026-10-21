using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

internal static class ProviderPresentationTests
{
    public static async Task RunAsync()
    {
        var clock = new Clock(); var api = new Api(clock); var browser = new Browser();
        await using (var vm = new ProviderLoginViewModel(api, browser, clock: clock, delay: clock.Delay))
        {
            var completed = 0; string? login = null;
            vm.Completed += json => { completed++; login = json; };
            await vm.StartAsync(); await vm.StartAsync();
            Check(completed == 1 && login!.Contains("requiresTwoFactor", StringComparison.Ordinal) && !vm.IsBusy && browser.Calls == 1 && vm.Countdown.Length > 0, "2FA/exactly-once/countdown");
        }
        var savedToken = ApiClientService.Instance.AuthToken;
        try
        {
            api = new(clock) { Wait = true }; browser = new();
            var vm = new ProviderLoginViewModel(api, browser, clock: clock, delay: (_, ct) => Task.Delay(Timeout.Infinite, ct));
            var window = new ProviderLoginWindow(model: vm);
            AssertAccessible(window);
            window.Show();
            await Until(() => browser.Calls == 1);
            ApiClientService.Instance.AuthToken = "provider-session-changed";
            await Until(() => !window.IsVisible);
            Check(vm.Status == "Сеанс изменён. Окно закрыто." && api.Cancels == 1 && !window.Succeeded, "session change did not close/cancel once");
            var linkApi = new Api(clock);
            var account = new EntraAccountLinkWindow(linkApi); AssertAccessible(account);
            account.Show();
            var status = account.FindControl<Button>("StatusButton")!;
            status.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => status.IsEnabled);
            Check(account.FindControl<Button>("LinkButton")!.IsEnabled && !account.FindControl<Button>("UnlinkButton")!.IsEnabled, "unlinked management state");
            linkApi.Linked = true;
            status.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => status.IsEnabled);
            Check(!account.FindControl<Button>("LinkButton")!.IsEnabled && account.FindControl<Button>("UnlinkButton")!.IsEnabled, "linked management state");
            account.FindControl<Button>("UnlinkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => status.IsEnabled);
            Check(linkApi.Unlinks == 1 && !account.FindControl<Button>("UnlinkButton")!.IsEnabled && !account.FindControl<Button>("LinkButton")!.IsEnabled, "uncertain unlink was retried/enabled");
            ApiClientService.Instance.AuthToken = "provider-next-session"; await Until(() => !account.IsVisible);
        }
        finally { ApiClientService.Instance.AuthToken = savedToken; }
        var selector = new LoginMethodSelectorWindow(); AssertAccessible(selector);
        Check(!selector.FindControl<Button>("EntraButton")!.IsVisible && !selector.FindControl<Button>("PasskeyButton")!.IsVisible, "unknown providers visible");
        selector.ApplyCapabilities(new(true, false, false, true));
        Check(selector.FindControl<Button>("EntraButton")!.IsVisible && !selector.FindControl<Button>("PasskeyButton")!.IsVisible, "server flags ignored");
        selector.ApplyCapabilities(new(false, false, false, true)); Check(!selector.FindControl<Button>("EntraButton")!.IsVisible, "disabled provider visible");
        selector.Close();
        try { await new SystemBrowserLauncher().OpenAsync(new Uri("http://evil.test")); throw new Exception("unsafe launcher URL accepted"); }
        catch (ProviderLoginException ex) { Check(ex.Code == "unsafe_url", "launcher safety"); }
        Console.WriteLine("PASS provider Avalonia headless: exactly-once/2FA/countdown, capability-hidden selectors, actual session-change closure/cancel, own-link status and uncertain unlink no retry, all interactive accessibility names, launcher unsafe URL rejection");
    }
    private static void AssertAccessible(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Where(control => control is Button or TextBox or ComboBox))
            Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), "missing interactive accessibility name: " + control.GetType().Name);
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(1, timeout.Token);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Provider presentation: " + message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public Task Delay(TimeSpan duration, CancellationToken ct) { ct.ThrowIfCancellationRequested(); now += duration; return Task.CompletedTask; }
    }
    private sealed class Browser : IBrowserLauncher
    { public int Calls; public Task OpenAsync(Uri url, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; } }
    private sealed class Api(Clock clock) : IProviderLoginApi
    {
        public string ApiBase => "https://api.test/base";
        public int Cancels, Unlinks; public bool Wait, Linked;
        public Task<ProviderCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProviderCapabilities(true, false, false, true));
        public Task<ProviderStart> StartAsync(string provider, string purpose, string? bearer = null, string? username = null, CancellationToken cancellationToken = default) => Task.FromResult(
            new ProviderStart(QrLoginProtocol.Base64Url(new byte[16]), QrLoginProtocol.Base64Url(new byte[32]), clock.GetUtcNow().AddMinutes(5), 500, "https://login.microsoftonline.com/authorize?state=private"));
        public Task<ProviderPoll> CompleteAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) => Task.FromResult(
            Wait ? new ProviderPoll("pending") : new ProviderPoll("completed", JsonSerializer.SerializeToElement(new { requiresTwoFactor = true, tempToken = "test-temp" })));
        public Task CancelAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) { Cancels++; return Task.CompletedTask; }
        public Task<ProviderLinkStatus> EntraLinkStatusAsync(string bearer, CancellationToken cancellationToken = default) => Task.FromResult(new ProviderLinkStatus(Linked ? "linked" : "unlinked"));
        public Task UnlinkEntraAsync(string bearer, CancellationToken cancellationToken = default) { Unlinks++; throw new ProviderLoginException("network_error"); }
    }
}
