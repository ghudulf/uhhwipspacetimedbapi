using System.Reflection;
using Serilog;
using System.Diagnostics;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public partial class AppShell : Shell
{
    private static readonly HashSet<string> _authenticatedRoutes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "main", "dashboard_tab", "dashboard",
            "routes_tab", "routes",
            "tickets_tab", "tickets",
            "profile_tab", "profile"
        };

    // ── Reflection: clear ShellSection._pendingNavigationRequest ────────
    // ShellSection throws "Pending Navigations still processing" when
    // RequestNavigation is called while a previous TCS is still alive.
    // This happens both during tab switches AND during initial GoToAsync
    // when ShellItemHandler.ConnectHandler calls UpdateTabs which calls
    // ToHandler on every ShellSection simultaneously.
    // Field name confirmed from runtime dump:
    // TaskCompletionSource`1 _handlerBasedNavigationCompletionSource
    private static readonly FieldInfo? _pendingNavField =
        FindField(typeof(ShellSection), "_handlerBasedNavigationCompletionSource");

    // Keep as secondary attempt with old name in case it varies across versions
    private static readonly FieldInfo? _navigationTaskField =
        FindField(typeof(ShellSection), "_navigationTask");

    private static FieldInfo? FindField(Type type, string name)
    {
        var t = type;
        while (t != null)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) return f;
            t = t.BaseType;
        }
        return null;
    }

    // ── Cooldown ─────────────────────────────────────────────────────────
    private static readonly TimeSpan _tabSwitchCooldown = TimeSpan.FromMilliseconds(150);
    private DateTime _lastNavigationCompletedAt = DateTime.MinValue;
    private bool _tabSwitchScheduled;

    // ── Other state ──────────────────────────────────────────────────────
    private string? _lastNavigatedLocation;
    private int _pendingProgrammaticNavigations;
    private bool? _tabBarVisible;

    // ────────────────────────────────────────────────────────────────────

    public AppShell()
    {
        Log.Debug("[AppShell] Constructor: start");
        Console.WriteLine("[AppShell] Constructor: start");
        Debug.WriteLine("[AppShell] Constructor: start");

        Log.Debug("[AppShell] Reflection: _pendingNavField={F1}, _navigationTaskField={F2}",
            _pendingNavField?.Name ?? "NOT FOUND",
            _navigationTaskField?.Name ?? "NOT FOUND");

        if (_pendingNavField == null && _navigationTaskField == null)
        {
            var fields = typeof(ShellSection)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(f => $"{f.FieldType.Name} {f.Name}");
            Log.Warning("[AppShell] ShellSection private fields: {Fields}", string.Join(", ", fields));
        }

        try
        {
            InitializeComponent();
            Log.Debug("[AppShell] Constructor: InitializeComponent complete");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[AppShell] Constructor FAILED");
            Console.Error.WriteLine($"[AppShell] Constructor FAILED: {ex}");
            throw;
        }
    }

    public void BeginProgrammaticNavigation() =>
        Interlocked.Increment(ref _pendingProgrammaticNavigations);

    /// <summary>
    /// Safe GoToAsync wrapper: clears all pending ShellSection TCS state,
    /// then navigates. On "Pending Navigations still processing", retries
    /// up to <paramref name="maxRetries"/> times with increasing delays.
    /// After a retry succeeds, forces the Avalonia ShellHandler to re-sync
    /// its visual tree by calling UpdateValue("CurrentItem").
    /// </summary>
    public async Task SafeGoToAsync(string route, int maxRetries = 5)
    {
        bool neededRetry = false;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                ClearAllPendingNavigations();

                if (neededRetry)
                {
                    // Before attempt 2: wait for _mainContentControl to be fully
                    // attached to the visual tree (VisualRoot != null, _presenter2 != null),
                    // then disable the CrossFade so UpdateContent calls HideOldPresenter()
                    // synchronously instead of starting a 250ms animation.
                    await WaitForMainContentControlReadyAsync();
                    await DisableMainContentTransitionAsync();
                }

                await GoToAsync(route);
                Log.Debug("[AppShell] SafeGoToAsync succeeded: {Route} (attempt {A})", route, attempt + 1);

                if (neededRetry)
                {
                    // Restore the transition after navigation completes
                    await RestoreMainContentTransitionAsync();
                }
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Pending Navigations"))
            {
                if (attempt == maxRetries)
                {
                    Log.Error(ex, "[AppShell] SafeGoToAsync: all {N} retries exhausted for {Route}", maxRetries + 1, route);
                    throw;
                }
                neededRetry = true;
                var delay = TimeSpan.FromMilliseconds(80 * Math.Pow(2, attempt));
                Log.Warning("[AppShell] SafeGoToAsync: PendingNavigations on attempt {A}, retrying in {Ms}ms → {Route}",
                    attempt + 1, delay.TotalMilliseconds, route);
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AppShell] SafeGoToAsync: unexpected error navigating to {Route}", route);
                throw;
            }
        }
    }

    /// <summary>
    /// Polls until the outer TransitioningContentControl (_mainContentControl) on the
    /// ShellHandler is fully attached to the visual tree AND its internal _presenter2
    /// field is non-null (i.e. the ControlTemplate has been applied).
    ///
    /// Without this guard, attempt 2 of SafeGoToAsync fires UpdateContent while
    /// _presenter2 is still null, causing the TCC to silently no-op and leaving
    /// the splash content permanently on screen.
    /// </summary>
    private async Task WaitForMainContentControlReadyAsync()
    {
        // VisualRoot is permanently null on _mainContentControl — the field exists but
        // the object is never attached to the Avalonia visual tree (it may be a
        // detached/shadow instance). Skip the poll and just do a fixed delay so
        // Avalonia has time to finish its layout pass before attempt 2 fires.
        // The DisableMainContentTransition + null PageTransition approach handles
        // the actual rendering fix; this method just provides the timing gap.
        try
        {
            var shellHandler = Handler;

            // One-time diagnostic: dump ShellHandler fields so we can identify
            // the correct field name if _mainContentControl is wrong
            if (shellHandler != null)
            {
                await Dispatcher.DispatchAsync(() =>
                {
                    try
                    {
                        var handlerType = shellHandler.GetType();
                        var allFields = new List<string>();
                        var t = handlerType;
                        while (t != null)
                        {
                            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                            {
                                var val = "(err)";
                                try { val = f.GetValue(shellHandler)?.GetType().Name ?? "null"; } catch { }
                                allFields.Add($"{f.DeclaringType?.Name}.{f.Name}={val}");
                            }
                            t = t.BaseType;
                        }
                        Log.Debug("[AppShell] ShellHandler fields: {Fields}", string.Join(" | ", allFields));

                        // Also try to find any Avalonia Control fields that have a VisualRoot
                        var outerField = FindField(handlerType, "_mainContentControl");
                        if (outerField != null)
                        {
                            var tcc = outerField.GetValue(shellHandler);
                            if (tcc != null)
                            {
                                var tccType = tcc.GetType();
                                var tccFields = new List<string>();
                                var tt = tccType;
                                while (tt != null)
                                {
                                    foreach (var f in tt.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                                    {
                                        var val = "(err)";
                                        try { val = f.GetValue(tcc)?.GetType().Name ?? "null"; } catch { }
                                        tccFields.Add($"{f.DeclaringType?.Name}.{f.Name}={val}");
                                    }
                                    tt = tt.BaseType;
                                }
                                Log.Debug("[AppShell] _mainContentControl ({Type}) fields: {Fields}",
                                    tccType.Name, string.Join(" | ", tccFields));

                                // Check all properties too
                                var props = tccType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                                var propDump = props.Select(p => {
                                    var v = "(err)";
                                    try { v = p.GetValue(tcc)?.ToString() ?? "null"; } catch { }
                                    return $"{p.Name}={v}";
                                });
                                Log.Debug("[AppShell] _mainContentControl props: {Props}", string.Join(" | ", propDump));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[AppShell] ShellHandler field dump failed");
                    }
                });
            }

            // Fixed delay — give Avalonia time to finish layout before attempt 2
            await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] WaitForMainContentControlReady failed (non-fatal), proceeding");
        }
    }

    // Saved transition for restore after retry
    private object? _savedMainContentTransition;

    private async Task DisableMainContentTransitionAsync()
    {
        try
        {
            var shellHandler = Handler;
            if (shellHandler == null) return;

            var outerField = FindField(shellHandler.GetType(), "_mainContentControl");
            if (outerField == null) return;
            var outerTcc = outerField.GetValue(shellHandler);
            if (outerTcc == null) return;

            var transitionProp = outerTcc.GetType().GetProperty("PageTransition",
                BindingFlags.Instance | BindingFlags.Public);
            if (transitionProp == null) return;

            await Dispatcher.DispatchAsync(() =>
            {
                _savedMainContentTransition = transitionProp.GetValue(outerTcc);
                transitionProp.SetValue(outerTcc, null);
                Log.Debug("[AppShell] DisableMainContentTransition: PageTransition nulled (was {T})",
                    _savedMainContentTransition?.GetType().Name ?? "null");
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] DisableMainContentTransition failed (non-fatal)");
        }
    }

    private async Task RestoreMainContentTransitionAsync()
    {
        try
        {
            var shellHandler = Handler;
            if (shellHandler == null) return;

            var outerField = FindField(shellHandler.GetType(), "_mainContentControl");
            if (outerField == null) return;
            var outerTcc = outerField.GetValue(shellHandler);
            if (outerTcc == null) return;

            var transitionProp = outerTcc.GetType().GetProperty("PageTransition",
                BindingFlags.Instance | BindingFlags.Public);
            if (transitionProp == null) return;

            // Wait one render pass so the null-transition UpdateContent fully commits
            await Task.Delay(50);

            await Dispatcher.DispatchAsync(() =>
            {
                transitionProp.SetValue(outerTcc, _savedMainContentTransition);
                _savedMainContentTransition = null;
                Log.Debug("[AppShell] RestoreMainContentTransition: PageTransition restored");
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] RestoreMainContentTransition failed (non-fatal)");
        }
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        try
        {
            var from = args.Current?.Location?.ToString() ?? string.Empty;
            var to   = args.Target?.Location?.ToString()  ?? string.Empty;

            bool isSelfNavigation = !string.IsNullOrEmpty(from) && from == to;
            bool isTabSyncReentry = !string.IsNullOrEmpty(_lastNavigatedLocation)
                && to == _lastNavigatedLocation && from == _lastNavigatedLocation;

            if (isSelfNavigation || isTabSyncReentry)
            {
                Log.Debug("[AppShell] OnNavigating (tab-sync, allowing): {From} -> {To}", from, to);
                base.OnNavigating(args);
                return;
            }

            // Clear stale TCS before Avalonia processes the navigation
            ClearAllPendingNavigations();

            // Cooldown: defer rapid successive tab switches
            var elapsed = DateTime.UtcNow - _lastNavigationCompletedAt;
            if (elapsed < _tabSwitchCooldown && !_tabSwitchScheduled && !string.IsNullOrEmpty(from))
            {
                var targetRoute = to;
                _tabSwitchScheduled = true;
                var delay = _tabSwitchCooldown - elapsed;

                Log.Debug("[AppShell] Cooldown active — deferring {To} by {Ms}ms", to, delay.TotalMilliseconds);
                args.Cancel();

                Dispatcher.DispatchDelayed(delay, async () =>
                {
                    _tabSwitchScheduled = false;
                    try { await SafeGoToAsync(targetRoute); }
                    catch (Exception ex) { Log.Warning(ex, "[AppShell] Deferred navigation to {Route} failed", targetRoute); }
                });
                return;
            }

            Log.Debug("[AppShell] OnNavigating: {From} -> {To}", from, to);
            base.OnNavigating(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppShell] OnNavigating threw unexpectedly");
            try { base.OnNavigating(args); } catch { /* swallow */ }
        }
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        try
        {
            if (_pendingProgrammaticNavigations > 0)
                Interlocked.Decrement(ref _pendingProgrammaticNavigations);

            _lastNavigatedLocation = args.Current?.Location?.ToString() ?? string.Empty;
            _lastNavigationCompletedAt = DateTime.UtcNow;

            Log.Debug("[AppShell] OnNavigated: {Location}", _lastNavigatedLocation);

            bool isAuthenticated = _authenticatedRoutes.Any(r =>
                _lastNavigatedLocation.Contains(r, StringComparison.OrdinalIgnoreCase));

            if (_tabBarVisible != isAuthenticated)
            {
                _tabBarVisible = isAuthenticated;
                Log.Debug("[AppShell] SetTabBarIsVisible: {Value}", isAuthenticated);
                Shell.SetTabBarIsVisible(this, isAuthenticated);
            }

            base.OnNavigated(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppShell] OnNavigated threw unexpectedly");
            try { base.OnNavigated(args); } catch { /* swallow */ }
        }
    }

    // ── Reflection helpers ───────────────────────────────────────────────

    /// <summary>
    /// Clears stale pending navigation TCS on every ShellSection in this Shell.
    /// Must be called before any GoToAsync to prevent "Pending Navigations still
    /// processing" from ShellSectionHandler.SyncNavigationStack.
    /// </summary>
    public void ClearAllPendingNavigations()
    {
        try
        {
            // Walk all items — Shell.Items contains ShellItem, Tab, ShellContent etc.
            // MAUI wraps bare ShellContent in implicit ShellSection (route "IMPL_xxx"),
            // so we must recurse into every possible container type.
            foreach (var item in Items)
                ClearPendingOnShellItem(item);

            // Also walk via visual tree descendants to catch implicit wrappers
            // that may not appear in Items directly
            foreach (var child in this.GetVisualTreeDescendants().OfType<ShellSection>())
                ClearPendingOnSection(child);
            foreach (var child in this.GetVisualTreeDescendants().OfType<ShellItem>())
                foreach (var section in child.Items)
                    ClearPendingOnSection(section);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ClearAllPendingNavigations failed (non-fatal)");
        }
    }

    private static void ClearPendingOnShellItem(BaseShellItem item)
    {
        try
        {
            switch (item)
            {
                case ShellItem shellItem:
                    foreach (var section in shellItem.Items)
                        ClearPendingOnSection(section);
                    break;
                case ShellSection section:
                    ClearPendingOnSection(section);
                    break;
                // bare ShellContent is wrapped by MAUI into an implicit ShellSection
                // accessible via its Parent after the Shell is built
                case ShellContent content when content.Parent is ShellSection parentSection:
                    ClearPendingOnSection(parentSection);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ClearPendingOnShellItem failed (non-fatal)");
        }
    }

    private static void ClearPendingOnSection(ShellSection section)
    {
        try
        {
            if (_pendingNavField != null)
            {
                var v = _pendingNavField.GetValue(section);
                if (v != null)
                {
                    Log.Debug("[AppShell] Cleared {Field} on section '{Route}'", _pendingNavField.Name, section.Route);
                    _pendingNavField.SetValue(section, null);
                }
            }

            if (_navigationTaskField != null)
            {
                var v = _navigationTaskField.GetValue(section);
                if (v != null)
                {
                    Log.Debug("[AppShell] Cleared {Field} on section '{Route}'", _navigationTaskField.Name, section.Route);
                    _navigationTaskField.SetValue(section, null);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ClearPendingOnSection failed for '{Route}' (non-fatal)", section.Route);
        }
    }
}
