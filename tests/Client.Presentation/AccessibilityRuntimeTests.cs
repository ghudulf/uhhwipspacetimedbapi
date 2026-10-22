using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

/// <summary>
/// Opens shipped AXAML windows on the real headless dispatcher, materializes Fluent control
/// templates and walks the visual tree (including populated item templates). Checks resolved
/// binding names, automation peers, tab eligibility and actual focus on enabled visible controls.
/// This cannot verify contrast, visual rendering, OS screen-reader announcements or native tab
/// traversal; those require real desktop/mobile platform validation with the shipped themes.
/// </summary>
internal static class AccessibilityRuntimeTests
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var app = Application.Current!;
        var theme = new FluentTheme();
        app.Styles.Add(theme);
        await using var server = new SyntheticServer();
        var api = ApiClientService.Instance;
        var baseField = typeof(ApiClientService).GetField("_discoveredBaseUrl", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var previousBase = baseField.GetValue(api);
        var previousToken = api.AuthToken;
        var previousTransport = Environment.GetEnvironmentVariable("BRU_TRANSPORT");
        try
        {
            baseField.SetValue(api, "http://192.0.2.1/api/");
            try { using var rejected = api.CreateClient(); throw new Exception("Legacy API client accepted remote plaintext credentials"); }
            catch (ArgumentException) { }
            baseField.SetValue(api, server.Origin + "api/");
            Environment.SetEnvironmentVariable("BRU_TRANSPORT", "http");
            api.AuthToken = "synthetic-accessibility-session";
            await CheckWindow(new LoginMethodSelectorWindow(), w => w.FindControl<Button>("EntraButton")!.IsVisible, cancellationToken);
            await CheckWindow(new QrLoginWindow(), w => w.DataContext is QrLoginViewModel { QrImage: not null }, cancellationToken);
            await CheckWindow(new QrDeviceLoginWindow(), _ => true, cancellationToken);
            await CheckWindow(new QrApproveWindow(), _ => true, cancellationToken);
            await CheckWindow(new QrDevicesWindow(), w => w.DataContext is QrDevicesViewModel { Devices.Count: 1 }, cancellationToken);
            await using (var provider = new ProviderLoginViewModel(new ProviderApi(server.Origin), new Browser(), delay: (_, ct) => Task.Delay(Timeout.Infinite, ct)))
                await CheckWindow(new ProviderLoginWindow(model: provider), _ => !string.IsNullOrEmpty(provider.Countdown), cancellationToken);
            await CheckWindow(new PasskeyCredentialsWindow(new PasskeyCredentialsViewModel(new PasskeyApi(), "synthetic", () => Task.FromResult(false))),
                w => w.DataContext is PasskeyCredentialsViewModel { Credentials.Count: 1 }, cancellationToken);
            await CheckWindow(new EntraAccountLinkWindow(new ProviderApi(server.Origin)), _ => true, cancellationToken);
            await CheckWindow(new WindowsAccountLinkWindow(), _ => true, cancellationToken);
            await CheckWindow(new CentralViewWindow(), w => w.DataContext is CentralViewWindowViewModel { IsConnected: true }, cancellationToken);

            // Exercise non-static accessible names against actual binding updates, not AXAML strings.
            var dialog = new ModalDialog();
            var model = (ModalDialogViewModel)dialog.DataContext!;
            model.PrimaryButtonText = "Продолжить"; model.SecondaryButtonText = "Отмена";
            model.ShowPrimaryButton = model.ShowSecondaryButton = true;
            await CheckWindow(dialog, _ => true, cancellationToken, async () =>
            {
                var button = dialog.FindControl<Button>("PrimaryButton")!;
                Check(AutomationProperties.GetName(button) == "Продолжить", "bound action name did not resolve");
                model.PrimaryButtonText = "Подтвердить";
                await Drain();
                Check(AutomationProperties.GetName(button) == "Подтвердить", "bound action name did not update");
                model.PrimaryButtonText = "";
                Check(Violations(dialog).Any(v => v.Contains("accessible name", StringComparison.Ordinal)), "empty runtime binding was not rejected");
                model.PrimaryButtonText = "Подтвердить";
                button.Focusable = false;
                Check(Violations(dialog).Any(v => v.Contains("focusable", StringComparison.Ordinal)), "nonfocusable action was not rejected");
                button.Focusable = true;
            });
        }
        finally
        {
            api.AuthToken = previousToken;
            baseField.SetValue(api, previousBase);
            Environment.SetEnvironmentVariable("BRU_TRANSPORT", previousTransport);
            app.Styles.Remove(theme);
        }
        Console.WriteLine("PASS accessibility runtime: 11 shipped windows opened; populated QR/passkey item templates, resolved/updated binding names, automation peers, tab eligibility and actual focus; negative runtime fixtures");
    }

    private static async Task CheckWindow(Window window, Func<Window, bool> ready, CancellationToken ct, Func<Task>? extra = null)
    {
        try
        {
            window.Show();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            while (!ready(window)) await Task.Delay(1, timeout.Token);
            await Drain();
            window.UpdateLayout();
            var controls = window.GetVisualDescendants().OfType<Control>().Where(IsInteractive).ToArray();
            Check(controls.Length > 0, window.GetType().Name + " did not materialize interactive visual children");
            var violations = Violations(window).ToArray();
            Check(violations.Length == 0, window.GetType().Name + ": " + string.Join("; ", violations));
            var focused = 0;
            foreach (var control in controls.Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && !TemplatePart(c) && !DelegatesItemFocus(c)))
            {
                Check(control.Focus(), window.GetType().Name + ": cannot focus " + control.GetType().Name + " " + control.Name);
                focused++;
            }
            Check(focused > 0, window.GetType().Name + " has no keyboard-focusable actions");
            if (extra != null) await extra();
            Console.WriteLine($"PASS accessibility visual tree {window.GetType().Name}: {controls.Length} interactive controls, {focused} focus checks");
        }
        finally { window.Close(); await Drain(); }
    }

    private static IEnumerable<string> Violations(Control root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Where(IsInteractive))
        {
            if (TemplatePart(control)) continue;
            var peer = ControlAutomationPeer.CreatePeerForElement(control);
            var name = AutomationProperties.GetName(control);
            var label = AutomationProperties.GetLabeledBy(control);
            if (peer == null) yield return "automation peer missing: " + control.GetType().Name + " " + control.Name;
            else if (string.IsNullOrWhiteSpace(peer.GetName()) && label == null) yield return "automation peer name missing: " + control.GetType().Name + " " + control.Name;
            if (string.IsNullOrWhiteSpace(name) && label == null) name = peer?.GetName();
            if (string.IsNullOrWhiteSpace(name) && label == null) yield return "accessible name missing: " + control.GetType().Name + " " + control.Name;
            if ((!control.Focusable || !KeyboardNavigation.GetIsTabStop(control)) && !DelegatesItemFocus(control)) yield return "not focusable/tab eligible: " + control.GetType().Name + " " + control.Name;
        }
    }

    private static bool IsInteractive(Control control) => AccessibilityPolicyTests.Interactive.Contains(control.GetType().Name);

    // Minimal template exceptions: Fluent's internal scrollbar parts/repeat buttons delegate to
    // the containing scroll area's peer and must not become additional application tab stops.
    // No application-owned controls, item templates or complete windows are excluded.
    private static bool TemplatePart(Control control) =>
        control is RepeatButton && control.TemplatedParent is Avalonia.Controls.Primitives.ScrollBar ||
        control is Avalonia.Controls.Primitives.ScrollBar && control.TemplatedParent is ScrollViewer &&
        control.Name is "PART_HorizontalScrollBar" or "PART_VerticalScrollBar";
    // Fluent ListBox is a tab-navigation container, not a tab stop itself. Its realized items
    // must remain named and focusable; they are checked individually and actually focused above.
    private static bool DelegatesItemFocus(Control control) => control is ListBox && control.GetVisualDescendants()
        .OfType<ListBoxItem>().Any(item => item.Focusable && KeyboardNavigation.GetIsTabStop(item));
    private static Task Drain() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Accessibility runtime: " + message); }

    private sealed class Browser : IBrowserLauncher
    { public Task OpenAsync(Uri url, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class ProviderApi(string origin) : IProviderLoginApi
    {
        public string ApiBase => origin;
        public Task<ProviderCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProviderCapabilities(true, true, false, true));
        public Task<ProviderStart> StartAsync(string provider, string purpose, string? bearer = null, string? username = null, CancellationToken cancellationToken = default) => Task.FromResult(
            new ProviderStart(QrLoginProtocol.Base64Url(new byte[16]), QrLoginProtocol.Base64Url(new byte[32]), DateTimeOffset.UtcNow.AddMinutes(5), 500, "https://login.microsoftonline.com/authorize"));
        public Task<ProviderPoll> CompleteAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) => Task.FromResult(new ProviderPoll("pending"));
        public Task CancelAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ProviderLinkStatus> EntraLinkStatusAsync(string bearer, CancellationToken cancellationToken = default) => Task.FromResult(new ProviderLinkStatus("unlinked"));
        public Task UnlinkEntraAsync(string bearer, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class PasskeyApi : IPasskeyManagementApi
    {
        public Task<PasskeyCredentialInfo[]> ListCredentialsAsync(string bearer, CancellationToken cancellationToken = default) => Task.FromResult<PasskeyCredentialInfo[]>([new("AQID", DateTime.UtcNow)]);
        public Task RemoveCredentialAsync(string bearer, string credentialId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Loopback-only synthetic responses for windows whose shipped constructors create their own
    // clients. Never inspect/log headers, request bodies or any real account/session material.
    private sealed class SyntheticServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task serving;
        public string Origin { get; }
        public SyntheticServer()
        {
            var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            Origin = $"http://127.0.0.1:{number}/";
            listener.Prefixes.Add(Origin); listener.Start();
            serving = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    var path = context.Request.Url!.AbsolutePath;
                    object body;
                    var id = QrLoginProtocol.Base64Url(new byte[16]);
                    var secret = QrLoginProtocol.Base64Url(new byte[32]);
                    if (path == "/api/auth/providers") body = new ProviderCapabilities(true, true, false, true);
                    else if (path == "/api/auth/profile") body = new { data = new { user = new { login = "synthetic" } } };
                    else if (path == "/api/session/access") body = new SessionAccess(true, false, [], []);
                    else if (path == "/api/auth/qr/devices") body = new[] { new QrDeviceInfo("synthetic-device", "Проверочное устройство", "test", 0, null) };
                    else if (path == "/api/auth/qr/sessions") body = new QrCreateResponse(id, secret, secret, QrLoginProtocol.BuildPayload(id, secret, Origin), "", "123456", DateTimeOffset.UtcNow.AddMinutes(5), 500);
                    else if (path.EndsWith("/status", StringComparison.Ordinal)) body = new QrPoll("pending");
                    else if (path.EndsWith("/cancel", StringComparison.Ordinal)) body = new { };
                    else throw new Exception("Unexpected synthetic accessibility API route: " + path);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(body, ApiProtocol.Json);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (!listener.IsListening && ex is HttpListenerException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync() { listener.Stop(); await serving; listener.Close(); }
    }
}
