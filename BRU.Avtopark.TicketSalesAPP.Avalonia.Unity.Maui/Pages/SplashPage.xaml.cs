using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        try
        {
            InitializeComponent();
            Log.Debug("[SplashPage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[SplashPage] Constructor FAILED");
            throw;
        }
    }

    protected override void OnAppearing()
    {
        try
        {
            Log.Debug("[SplashPage] OnAppearing");
            base.OnAppearing();
            Dispatcher.DispatchAsync(RunStartupFlow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] OnAppearing threw");
        }
    }

    protected override void OnDisappearing()
    {
        try
        {
            Log.Information("[SplashPage] OnDisappearing — splash is being hidden");
            base.OnDisappearing();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] OnDisappearing threw");
        }
    }

    private bool _navigationStarted;
    private bool _startupFlowCompleted;

    private async Task RunStartupFlow()
    {
        if (_navigationStarted || _startupFlowCompleted) return;
        _navigationStarted = true;

        try
        {
            Log.Information("[SplashPage] RunStartupFlow: start");
            StatusLabel.Text = "Проверка авторизации...";

            // Poll until the Shell handler is fully settled before firing navigation.
            // The Shell initializes handlers for all ShellContent items on first render;
            // navigating too early causes "Pending Navigations still processing".
            // We detect readiness by checking that all ShellSection TCS fields are null
            // (i.e. no pending navigation is in flight).
            await WaitForShellHandlerReadyAsync();

            bool hasValidToken = await MauiAuthService.Instance.HasValidTokenAsync();
            Log.Information("[SplashPage] HasValidToken={HasToken}", hasValidToken);

            if (hasValidToken)
            {
                Log.Information("[SplashPage] Valid token found, navigating to main");
                Console.WriteLine("[SplashPage] Valid token found, navigating to main");
                StatusLabel.Text = "Добро пожаловать!";
                await Task.Delay(300);
                await NavigateProgrammaticallyAsync("//main");
            }
            else
            {
                Log.Information("[SplashPage] No valid token, navigating to login");
                Console.WriteLine("[SplashPage] No valid token, navigating to login");
                await NavigateProgrammaticallyAsync("//login");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] Startup flow error");
            Console.Error.WriteLine($"[SplashPage] Startup flow error: {ex}");
            try
            {
                StatusLabel.Text = "Ошибка запуска";
                Spinner.IsRunning = false;
                await Task.Delay(1500);
                await NavigateProgrammaticallyAsync("//login");
            }
            catch (Exception inner)
            {
                Log.Error(inner, "[SplashPage] Recovery navigation also failed");
            }
        }
        finally
        {
            _navigationStarted = false;
            _startupFlowCompleted = true;
        }
    }

    /// <summary>
    /// Polls until all ShellSection pending-navigation TCS fields are null,
    /// meaning the Shell handler has finished its initial ConnectHandler setup.
    /// Falls back to a fixed delay if reflection isn't available.
    /// </summary>
    private static async Task WaitForShellHandlerReadyAsync()
    {
        const int pollMs = 50;
        const int maxWaitMs = 5000;
        int elapsed = 0;

        // Minimum wait — give the Shell at least one render pass
        await Task.Delay(200);
        elapsed += 200;

        try
        {
            if (Shell.Current is not AppShell appShell)
            {
                Log.Warning("[SplashPage] WaitForShellHandlerReady: Shell.Current is not AppShell, falling back to fixed delay");
                await Task.Delay(1000);
                return;
            }

            var pendingNavField = typeof(ShellSection)
                .GetField("_handlerBasedNavigationCompletionSource",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (pendingNavField == null)
            {
                Log.Warning("[SplashPage] WaitForShellHandlerReady: reflection field not found, falling back to fixed delay");
                await Task.Delay(1000);
                return;
            }

            while (elapsed < maxWaitMs)
            {
                bool anyPending = false;
                string? pendingRoute = null;
                try
                {
                    // Collect ALL ShellSections — including implicit IMPL_xxx wrappers
                    // that MAUI creates for bare ShellContent items. These appear as
                    // children of ShellItem.Items but the ShellItem itself may be an
                    // implicit wrapper too. Walk the full tree.
                    var allSections = GetAllShellSections(appShell);
                    foreach (var section in allSections)
                    {
                        var tcs = pendingNavField.GetValue(section);
                        if (tcs != null)
                        {
                            anyPending = true;
                            pendingRoute = section.Route;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[SplashPage] WaitForShellHandlerReady: poll threw (non-fatal)");
                    break;
                }

                if (!anyPending)
                {
                    Log.Debug("[SplashPage] WaitForShellHandlerReady: Shell ready after {Ms}ms", elapsed);
                    return;
                }

                Log.Debug("[SplashPage] WaitForShellHandlerReady: section '{Route}' still pending at {Ms}ms", pendingRoute, elapsed);
                await Task.Delay(pollMs);
                elapsed += pollMs;
            }

            Log.Warning("[SplashPage] WaitForShellHandlerReady: timed out after {Ms}ms, proceeding anyway", elapsed);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SplashPage] WaitForShellHandlerReady failed, falling back to fixed delay");
            await Task.Delay(800);
        }
    }

    private static IEnumerable<ShellSection> GetAllShellSections(Shell shell)
    {
        // Walk Items (may include implicit ShellItem wrappers for bare ShellContent)
        foreach (var item in shell.Items)
        {
            foreach (var section in item.Items)
                yield return section;
        }
        // Also walk via GetVisualTreeDescendants to catch any implicit wrappers
        // that don't appear in Items (MAUI implementation detail)
        foreach (var child in shell.GetVisualTreeDescendants().OfType<ShellSection>())
            yield return child;
    }

    private static async Task NavigateProgrammaticallyAsync(string route)
    {
        try
        {
            Log.Debug("[SplashPage] NavigateProgrammaticallyAsync → {Route}", route);
            if (Shell.Current is AppShell appShell)
            {
                appShell.BeginProgrammaticNavigation();
                await appShell.SafeGoToAsync(route);
            }
            else
            {
                await Shell.Current.GoToAsync(route);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] NavigateProgrammaticallyAsync to {Route} failed", route);
            throw;
        }
    }
}
