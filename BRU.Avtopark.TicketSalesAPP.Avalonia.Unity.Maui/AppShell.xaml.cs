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
                await GoToAsync(route);
                Log.Debug("[AppShell] SafeGoToAsync succeeded: {Route} (attempt {A})", route, attempt + 1);

                if (neededRetry)
                {
                    // Attempt 1 partially ran ShellItemHandler.ConnectHandler which left
                    // the Avalonia visual tree in a stale state. Force the handler to
                    // re-sync CurrentItem so the correct page is actually displayed.
                    await ForceHandlerResyncAsync(route);
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

    private async Task ForceHandlerResyncAsync(string targetRoute)
    {
        // Root cause (confirmed from Avalonia source + logs):
        //
        // ShellHandler.CreatePlatformView() builds:
        //   ContentPage → DrawerPage → DockPanel → _mainContentControl (TransitioningContentControl)
        // This ContentPage is placed into the Avalonia NavigationPage stack by StackNavigationManager.
        //
        // ShellItemHandler.CreatePlatformElement() builds:
        //   _contentControl (TransitioningContentControl) — single section case
        // ShellHandler.UpdateCurrentItem sets _mainContentControl.Content = ShellItemHandler.PlatformView
        //
        // After attempt 1 partially ran ConnectHandler, the NavigationPage stack has the Shell's
        // ContentPage wrapper. _mainContentControl.Content IS being set correctly (logs confirm:
        // "Content = TransitioningContentControl"). But the screen still shows SplashPage because
        // the ShellItemHandler._contentControl (inner TCC) has its own stale presenter state from
        // the aborted attempt 1 transition — the CrossFade left PART_PresentingContent showing
        // SplashPage's NavigationPage content on top.
        //
        // Fix: after UpdateValue creates a fresh ShellItemHandler with a fresh _contentControl,
        // cycle _mainContentControl.Content (null → value) with PageTransition=null to force
        // TransitioningContentControl to swap presenters synchronously without animation.
        // This clears any stale presenter state from the aborted attempt 1.
        try
        {
            Log.Debug("[AppShell] ForceHandlerResync: starting for {Route}", targetRoute);
            await Task.Delay(100);

            var shellHandler = Handler;
            if (shellHandler == null)
            {
                Log.Warning("[AppShell] ForceHandlerResync: Handler is null, skipping");
                return;
            }

            // Step 1: Clear TCS
            ClearAllPendingNavigations();

            // Step 2: Disconnect cached ShellItemHandler so ToHandler creates a fresh one
            TryDisconnectCurrentItemHandler(shellHandler);

            // Step 3: Null _currentItemHandler so UpdateCurrentItem doesn't short-circuit
            TryClearShellHandlerCurrentItem(shellHandler);

            // Step 4: Null _mainContentControl.Content to release stale platform view
            TryClearMainContentControl(shellHandler);

            // Step 5: Clear TCS again before UpdateValue
            ClearAllPendingNavigations();
            Log.Debug("[AppShell] ForceHandlerResync: state cleared, calling UpdateValue");

            // Step 6: Trigger MapCurrentItem → UpdateCurrentItem → fresh ShellItemHandler
            shellHandler.UpdateValue(nameof(CurrentItem));
            Log.Debug("[AppShell] ForceHandlerResync: UpdateValue(CurrentItem) called");

            // Step 7: Let the handler settle, then clear TCS
            await Task.Delay(30);
            ClearAllPendingNavigations();

            // Step 8: Log state + force-cycle _mainContentControl to flush stale TCC presenter
            TryLogMainContentControlState(shellHandler);
            await TryForceTransitioningContentControlRefreshAsync(shellHandler);

            // Step 9: Nudge layout
            try { InvalidateMeasure(); } catch { /* non-fatal */ }

            Log.Debug("[AppShell] ForceHandlerResync: complete");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ForceHandlerResync failed (non-fatal)");
        }
    }

    /// <summary>
    /// Forces <see cref="TransitioningContentControl"/> (_mainContentControl on ShellHandler)
    /// to swap its internal presenters by cycling Content: null → value with PageTransition
    /// temporarily nulled. We await a dispatcher yield between null and restore so the TCC's
    /// async transition machinery (which posts to UIThread) actually processes the null before
    /// we set the real content — otherwise the synchronous null+set is a no-op from TCC's POV.
    /// </summary>
    private static async Task TryForceTransitioningContentControlRefreshAsync(IElementHandler shellHandler)
    {
        try
        {
            var handlerType = shellHandler.GetType();
            var field = FindField(handlerType, "_mainContentControl");
            if (field == null)
            {
                Log.Debug("[AppShell] TCC-Refresh: _mainContentControl field not found");
                return;
            }

            var tcc = field.GetValue(shellHandler);
            if (tcc == null)
            {
                Log.Debug("[AppShell] TCC-Refresh: _mainContentControl is null");
                return;
            }

            var tccType = tcc.GetType();

            var contentProp = tccType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);
            if (contentProp == null)
            {
                Log.Debug("[AppShell] TCC-Refresh: Content property not found on {Type}", tccType.Name);
                return;
            }

            var currentContent = contentProp.GetValue(tcc);
            if (currentContent == null)
            {
                Log.Debug("[AppShell] TCC-Refresh: Content is null, nothing to cycle");
                return;
            }

            var transitionProp = tccType.GetProperty("PageTransition", BindingFlags.Instance | BindingFlags.Public);
            var savedTransition = transitionProp?.GetValue(tcc);

            Log.Debug("[AppShell] TCC-Refresh: cycling Content on {Type} (PageTransition={T})",
                tccType.Name, savedTransition?.GetType().Name ?? "null");

            // Diagnose: is the TCC actually in the visual tree?
            try
            {
                var visualRootProp = tccType.GetProperty("VisualRoot",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var visualRoot = visualRootProp?.GetValue(tcc);
                Log.Debug("[AppShell] TCC-Refresh: TCC.VisualRoot={VR}", visualRoot?.GetType().Name ?? "null");
            }
            catch { /* non-fatal */ }

            // Null PageTransition so no CrossFade fires during the cycle
            transitionProp?.SetValue(tcc, null);

            // Set null — TCC queues its transition work on UIThread.Post
            contentProp.SetValue(tcc, null);

            // Yield to the UI thread so TCC's posted work runs and presenters reset
            await Task.Delay(50);

            // Now set the real content — TCC will show it instantly (no transition)
            contentProp.SetValue(tcc, currentContent);

            // Another yield so the content render pass completes before we restore transition
            await Task.Delay(50);

            // Restore PageTransition for future navigations
            transitionProp?.SetValue(tcc, savedTransition);

            // Verify content is still set after cycle
            var contentAfter = contentProp.GetValue(tcc);
            Log.Debug("[AppShell] TCC-Refresh: Content after cycle = {Type}", contentAfter?.GetType().Name ?? "null");

            // Also try to force-invalidate the inner ShellItemHandler._contentControl
            // The outer TCC holds ShellItemHandler.PlatformView as its content.
            // ShellItemHandler.PlatformView is itself a TransitioningContentControl (_contentControl).
            // We need to also cycle THAT inner TCC to flush its presenter state.
            if (contentAfter != null)
            {
                TryRefreshInnerShellItemTcc(contentAfter);
            }

            Log.Debug("[AppShell] TCC-Refresh: Content cycled, PageTransition restored");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] TCC-Refresh failed (non-fatal)");
        }
    }

    private static void TryRefreshInnerShellItemTcc(object outerContent)
    {
        // outerContent = ShellItemHandler.PlatformView
        // For single-section Shell items this is ShellItemHandler._contentControl (a TCC).
        // For multi-section it's a TabbedPage — skip.
        try
        {
            var type = outerContent.GetType();
            var typeName = type.Name;
            Log.Debug("[AppShell] InnerTCC: outerContent type = {T}", typeName);

            // Single-section: PlatformView IS the _contentControl (TransitioningContentControl)
            // Multi-section: PlatformView is TabbedPage — has no Content property to cycle
            var contentProp = type.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);
            if (contentProp == null)
            {
                Log.Debug("[AppShell] InnerTCC: no Content property on {T}, skipping", typeName);
                return;
            }

            var innerContent = contentProp.GetValue(outerContent);
            Log.Debug("[AppShell] InnerTCC: inner Content = {T}", innerContent?.GetType().Name ?? "null");

            if (innerContent == null) return;

            var transitionProp = type.GetProperty("PageTransition", BindingFlags.Instance | BindingFlags.Public);
            var savedTransition = transitionProp?.GetValue(outerContent);

            // Cycle with no transition
            transitionProp?.SetValue(outerContent, null);
            contentProp.SetValue(outerContent, null);
            contentProp.SetValue(outerContent, innerContent);
            transitionProp?.SetValue(outerContent, savedTransition);

            Log.Debug("[AppShell] InnerTCC: cycled inner Content ({T})", innerContent.GetType().Name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] InnerTCC: failed (non-fatal)");
        }
    }

    private static void TryDisconnectCurrentItemHandler(IElementHandler shellHandler)
    {
        try
        {
            // Get the cached ShellItemHandler from ShellHandler._currentItemHandler
            // OR directly from CurrentItem.Handler — whichever is set.
            // We need to call DisconnectHandler on it so MAUI clears the handler cache
            // on the ShellItem element, forcing ToHandler to create a fresh one.
            var handlerType = shellHandler.GetType();
            var field = FindField(handlerType, "_currentItemHandler");
            object? existingItemHandler = field?.GetValue(shellHandler);

            if (existingItemHandler == null)
            {
                // Try getting it directly from the ShellItem's Handler property
                if (shellHandler is IElementHandler eh && eh.VirtualView is Shell shell && shell.CurrentItem?.Handler != null)
                    existingItemHandler = shell.CurrentItem.Handler;
            }

            if (existingItemHandler is IElementHandler itemHandler)
            {
                Log.Debug("[AppShell] ForceHandlerResync: disconnecting existing ShellItemHandler ({Type})", existingItemHandler.GetType().Name);
                try { itemHandler.DisconnectHandler(); }
                catch (Exception ex) { Log.Warning(ex, "[AppShell] ForceHandlerResync: DisconnectHandler on ShellItemHandler threw (non-fatal)"); }
            }
            else
            {
                Log.Debug("[AppShell] ForceHandlerResync: no existing ShellItemHandler to disconnect");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ForceHandlerResync: TryDisconnectCurrentItemHandler failed (non-fatal)");
        }
    }

    private static bool TryClearShellHandlerCurrentItem(IElementHandler handler)
    {
        try
        {
            // ShellHandler (Avalonia.Controls.Maui.Handlers.Shell.ShellHandler) has:
            //   internal ShellItemHandler? _currentItemHandler
            // Nulling it forces UpdateCurrentItem to create a fresh handler and
            // set _mainContentControl.Content to the correct platform view.
            var handlerType = handler.GetType();
            var field = FindField(handlerType, "_currentItemHandler");
            if (field == null)
            {
                Log.Warning("[AppShell] ForceHandlerResync: _currentItemHandler field not found on {Type}", handlerType.Name);
                // Log all fields for diagnosis
                var fields = handlerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Select(f => $"{f.FieldType.Name} {f.Name}");
                Log.Debug("[AppShell] ForceHandlerResync: {Type} fields: {Fields}", handlerType.Name, string.Join(", ", fields));
                return false;
            }

            var existing = field.GetValue(handler);
            if (existing != null)
            {
                field.SetValue(handler, null);
                Log.Debug("[AppShell] ForceHandlerResync: cleared _currentItemHandler (was {Type})", existing.GetType().Name);
            }
            else
            {
                Log.Debug("[AppShell] ForceHandlerResync: _currentItemHandler was already null");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ForceHandlerResync: TryClearShellHandlerCurrentItem failed (non-fatal)");
            return false;
        }
    }

    private static void TryClearMainContentControl(IElementHandler handler)
    {
        try
        {
            // ShellHandler._mainContentControl is a TransitioningContentControl.
            // Null its Content so the stale platform view is released before
            // the new ShellItemHandler sets its own content via UpdateCurrentItem.
            var handlerType = handler.GetType();
            var field = FindField(handlerType, "_mainContentControl");
            if (field == null)
            {
                Log.Debug("[AppShell] ForceHandlerResync: _mainContentControl field not found (non-fatal)");
                return;
            }
            var contentControl = field.GetValue(handler);
            if (contentControl == null) return;

            // TransitioningContentControl has a Content property — set it to null
            var contentProp = contentControl.GetType().GetProperty("Content",
                BindingFlags.Instance | BindingFlags.Public);
            if (contentProp != null)
            {
                contentProp.SetValue(contentControl, null);
                Log.Debug("[AppShell] ForceHandlerResync: _mainContentControl.Content cleared");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] ForceHandlerResync: TryClearMainContentControl failed (non-fatal)");
        }
    }

    private static void TryLogMainContentControlState(IElementHandler handler)
    {
        try
        {
            var handlerType = handler.GetType();
            var field = FindField(handlerType, "_mainContentControl");
            if (field == null) { Log.Debug("[AppShell] Diag: _mainContentControl field not found"); return; }
            var contentControl = field.GetValue(handler);
            if (contentControl == null) { Log.Debug("[AppShell] Diag: _mainContentControl is null"); return; }

            var contentProp = contentControl.GetType().GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);
            var content = contentProp?.GetValue(contentControl);
            Log.Debug("[AppShell] Diag: _mainContentControl.Content = {Type}", content?.GetType().Name ?? "null");

            // Also log _currentItemHandler after UpdateValue
            var itemHandlerField = FindField(handlerType, "_currentItemHandler");
            var itemHandler = itemHandlerField?.GetValue(handler);
            Log.Debug("[AppShell] Diag: _currentItemHandler after UpdateValue = {Type}", itemHandler?.GetType().Name ?? "null");

            // If content is still null after UpdateValue, try to set it directly
            // by getting the platform view from the new _currentItemHandler
            if (content == null && itemHandler != null)
            {
                Log.Warning("[AppShell] Diag: _mainContentControl.Content still null after UpdateValue — forcing direct set");
                TryForceSetMainContent(contentControl, contentProp, itemHandler);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] Diag: TryLogMainContentControlState failed (non-fatal)");
        }
    }

    private static void TryForceSetMainContent(object contentControl, System.Reflection.PropertyInfo? contentProp, object itemHandler)
    {
        try
        {
            // Get the platform view from the ShellItemHandler
            var platformViewProp = itemHandler.GetType().GetProperty("PlatformView",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (platformViewProp == null)
            {
                // Try base type
                var t = itemHandler.GetType().BaseType;
                while (t != null && platformViewProp == null)
                {
                    platformViewProp = t.GetProperty("PlatformView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    t = t.BaseType;
                }
            }
            var platformView = platformViewProp?.GetValue(itemHandler);
            Log.Debug("[AppShell] Diag: ShellItemHandler.PlatformView = {Type}", platformView?.GetType().Name ?? "null");

            if (platformView != null && contentProp != null)
            {
                contentProp.SetValue(contentControl, platformView);
                Log.Debug("[AppShell] Diag: forced _mainContentControl.Content = {Type}", platformView.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppShell] Diag: TryForceSetMainContent failed (non-fatal)");
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

            // Also walk via LogicalChildren which includes implicit wrappers
            // that may not appear in Items directly
            foreach (var child in LogicalChildren.OfType<ShellSection>())
                ClearPendingOnSection(child);
            foreach (var child in LogicalChildren.OfType<ShellItem>())
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
