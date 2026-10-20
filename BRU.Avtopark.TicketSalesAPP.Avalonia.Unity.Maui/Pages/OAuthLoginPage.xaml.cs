using System.Threading.Channels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

/// <summary>
/// MAUI ContentPage hosting the Avalonia <see cref="OAuthLoginControl"/> via AvaloniaView.
///
/// Architecture: Avalonia control → Channel → async pipeline → Shell navigation
///
/// The Avalonia AuthCompleted event fires on the Avalonia UI thread. We cannot use
/// Microsoft.Maui.ApplicationModel.MainThread (not implemented in Avalonia.Controls.Maui
/// desktop backend) and we cannot use Avalonia.Threading.Dispatcher (not available in
/// the MAUI project). Instead, the event handler writes the result into a bounded
/// Channel&lt;OAuthResult&gt;. A long-running async Task started in OnAppearing reads
/// from that channel and drives the token exchange + Shell navigation entirely on the
/// thread-pool — no UI thread marshalling required at any point.
/// </summary>
public partial class OAuthLoginPage : ContentPage
{
    private readonly MauiAuthService _auth = MauiAuthService.Instance;

    // Single-item bounded channel: Avalonia thread writes, pipeline task reads.
    // BoundedChannelFullMode.DropOldest ensures a stale result never blocks the writer.
    private readonly Channel<OAuthResult> _resultChannel =
        Channel.CreateBounded<OAuthResult>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

    private CancellationTokenSource? _pipelineCts;

    public OAuthLoginPage()
    {
        InitializeComponent();
        OAuthControl.AuthCompleted += OnAvaloniaAuthCompleted;
        Log.Information("[OAuthLoginPage] Initialized");
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log.Information("[OAuthLoginPage] OnAppearing");

        // Cancel any previous pipeline (e.g. user navigated back and returned)
        _pipelineCts?.Cancel();
        _pipelineCts = new CancellationTokenSource();

        // Start the two concurrent tasks: flow setup + result pipeline
        _ = SetupOAuthFlowAsync();
        _ = RunAuthPipelineAsync(_pipelineCts.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pipelineCts?.Cancel();
        Log.Information("[OAuthLoginPage] OnDisappearing — pipeline cancelled");
    }

    // ── OAuth flow setup ─────────────────────────────────────────────────

    private async Task SetupOAuthFlowAsync()
    {
        try
        {
            var scopes = new[] { "openid", "profile", "email", "offline_access", "api" };
            var authUrl = _auth.OAuthService.GenerateAuthorizationUrl(scopes, out var state, out var codeVerifier);

            Log.Information("[OAuthLoginPage] Auth URL generated, state={State}", state);

            // Redirect URI must always be localhost — registered in OpenIddict on the server.
            // The discovered LAN IP is only for authorize/token endpoints.
            var redirectUri = "http://localhost:5000/callback";

            OAuthControl.AuthorizationUrl = authUrl;
            OAuthControl.RedirectUri      = redirectUri;
            OAuthControl.ExpectedState    = state;
            OAuthControl.CodeVerifier     = codeVerifier;

            await OAuthControl.StartFlowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OAuthLoginPage] SetupOAuthFlowAsync failed");
            // Write a synthetic failure into the channel so the pipeline can show the error
            _resultChannel.Writer.TryWrite(new OAuthResult { Success = false, Error = ex.Message });
        }
    }

    // ── Avalonia event → channel ─────────────────────────────────────────

    /// <summary>
    /// Called on the Avalonia UI thread. Must be synchronous and non-blocking.
    /// When auth succeeds, immediately shows the MAUI loading overlay so the user
    /// never sees the WebView "navigation failed" error while the token exchange runs.
    /// Then writes the result into the channel; the pipeline task picks it up.
    /// </summary>
    private void OnAvaloniaAuthCompleted(object? sender, OAuthResult result)
    {
        Log.Information("[OAuthLoginPage] AuthCompleted received: success={S}, error={E}",
            result.Success, result.Error);

        if (result.Success)
        {
            // Show the MAUI overlay immediately on the MAUI UI thread.
            // Dispatcher.Dispatch is the only safe cross-thread UI call on the
            // Avalonia.Controls.Maui desktop backend (MainThread.* is broken there).
            Dispatcher.Dispatch(() =>
            {
                ErrorLabel.IsVisible    = false;  // hide any previous error
                LoadingOverlay.IsVisible = true;
            });
        }

        // TryWrite is lock-free and non-blocking — safe to call from any thread
        bool written = _resultChannel.Writer.TryWrite(result);
        if (!written)
            Log.Warning("[OAuthLoginPage] Channel write dropped (full) — result lost");
    }

    // ── Pipeline: channel → token exchange → navigation ──────────────────

    /// <summary>
    /// Runs entirely on the thread-pool. Reads one OAuthResult from the channel,
    /// performs the token exchange (pure async HTTP), then calls Shell.GoToAsync.
    /// Shell navigation is safe to call from any thread in MAUI.
    /// </summary>
    private async Task RunAuthPipelineAsync(CancellationToken ct)
    {
        Log.Information("[OAuthLoginPage] Auth pipeline started");
        try
        {
            // Wait for the Avalonia control to deliver a result
            var result = await _resultChannel.Reader.ReadAsync(ct);

            Log.Information("[OAuthLoginPage] Pipeline received result: success={S}", result.Success);

            if (!result.Success)
            {
                if (result.Error != "user_cancelled")
                {
                    Log.Warning("[OAuthLoginPage] Auth failed: {Error}", result.Error);
                    SetErrorLabel($"Ошибка авторизации: {result.Error}");
                }
                return;
            }

            // Token exchange — pure async HTTP, no UI thread needed
            Log.Information("[OAuthLoginPage] Exchanging code (len={L}) with verifier (len={V})",
                result.Code?.Length ?? 0, result.CodeVerifier?.Length ?? 0);

            var tokens = await _auth.ExchangeAndPersistAsync(result.Code!, result.CodeVerifier!);

            if (tokens is not null)
            {
                Log.Information("[OAuthLoginPage] Token exchange OK — expires {Exp:u}", tokens.ExpiresAt);
                // Shell.GoToAsync is thread-safe in MAUI
                await AppShell.NavigateToMainAsync();
            }
            else
            {
                Log.Warning("[OAuthLoginPage] ExchangeAndPersistAsync returned null");
                SetErrorLabel("Обмен кода на токен не удался. Проверьте подключение к серверу.");
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("[OAuthLoginPage] Pipeline cancelled (page navigated away)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OAuthLoginPage] Pipeline exception");
            SetErrorLabel($"Ошибка: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the error label and hides the loading overlay.
    /// Uses the MAUI IDispatcher obtained from the page itself —
    /// this is the only safe way to touch MAUI UI from a background thread on the
    /// Avalonia.Controls.Maui desktop backend (avoids the broken MainThread static).
    /// </summary>
    private void SetErrorLabel(string message)
    {
        Log.Warning("[OAuthLoginPage] Showing error: {Msg}", message);
        Dispatcher.Dispatch(() =>
        {
            LoadingOverlay.IsVisible = false;
            ErrorLabel.Text      = message;
            ErrorLabel.IsVisible = true;
        });
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Debug("[OAuthLoginPage] Back clicked");
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OAuthLoginPage] OnBackClicked navigation failed");
        }
    }
}
