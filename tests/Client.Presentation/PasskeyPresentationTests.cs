using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

internal static class PasskeyPresentationTests
{
    public static async Task RunAsync()
    {
        var api = new Api(); var registrations = 0; var result = true;
        await using (var vm = new PasskeyCredentialsViewModel(api, "synthetic", () => { registrations++; return Task.FromResult(result); }))
        {
            Check(!vm.CanMutate, "mutations allowed before own-list check");
            await vm.RefreshAsync(); Check(vm.CanMutate && vm.Credentials.Count == 1, "own-list presentation");
            await vm.RegisterAsync(); Check(registrations == 1 && vm.CanMutate, "browser registration/list refresh");
            result = false; await vm.RegisterAsync(); await vm.RegisterAsync();
            Check(registrations == 2 && !vm.CanMutate && vm.Status.Contains("не подтверждён", StringComparison.Ordinal), "uncertain registration retried");
            await vm.RefreshAsync(); api.FailRemoval = true;
            var item = vm.Credentials.Single(); await vm.RemoveAsync(item); await vm.RemoveAsync(item);
            Check(api.Removals == 1 && !vm.CanMutate, "uncertain deletion retried");
            api.FailRemoval = false; await vm.RefreshAsync(); await vm.RemoveAsync(vm.Credentials.Single());
            Check(api.Removals == 2 && vm.Credentials.Count == 0 && vm.CanMutate, "committed removal/list refresh");
            vm.SessionChanged(); var reads = api.Reads; await vm.RefreshAsync(); await vm.RegisterAsync();
            Check(api.Reads == reads && !vm.CanMutate && vm.Credentials.Count == 0, "stale session retained keys or network work");
        }
        var token = ApiClientService.Instance.AuthToken;
        try
        {
            var window = new PasskeyCredentialsWindow(new PasskeyCredentialsViewModel(new Api(), "synthetic", () => Task.FromResult(false)));
            window.Show(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            foreach (var control in window.GetLogicalDescendants().OfType<Control>().Where(control => control is Button or TextBox or ComboBox))
                Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), "missing interactive accessibility name");
            ApiClientService.Instance.AuthToken = "passkey-session-change";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Check(!window.IsVisible && window.DataContext is PasskeyCredentialsViewModel { Status: "Сеанс изменён. Окно закрыто." }, "session event did not close keys window");
        }
        finally { ApiClientService.Instance.AuthToken = token; }
        var selector = new LoginMethodSelectorWindow();
        Check(!selector.FindControl<Button>("PasskeyButton")!.IsVisible, "unknown capability shown");
        selector.ApplyCapabilities(new(false, true, false, false));
        Check(selector.FindControl<Button>("PasskeyButton")!.IsVisible && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(selector.FindControl<Button>("PasskeyButton")!)), "passkey capability/accessibility");
        selector.Close();
        Console.WriteLine("PASS passkey Avalonia headless: capability-gated selector, own-key list/register/remove, uncertain outcomes never retried, explicit recheck, session-event closure/data clearing and interactive accessibility names");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Passkey presentation: " + message); }
    private sealed class Api : IPasskeyManagementApi
    {
        public int Reads, Removals; public bool FailRemoval;
        private bool removed;
        public Task<PasskeyCredentialInfo[]> ListCredentialsAsync(string bearer, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult<PasskeyCredentialInfo[]>(removed ? [] : [new("AQID", DateTime.UtcNow)]); }
        public Task RemoveCredentialAsync(string bearer, string credentialId, CancellationToken cancellationToken = default)
        { Removals++; if (FailRemoval) throw new ProviderLoginException("network_error"); removed = true; return Task.CompletedTask; }
    }
}
