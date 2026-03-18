using Avalonia.Controls;
using Avalonia.Controls.Maui.Handlers;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using MainThread = Microsoft.Maui.ApplicationModel.MainThread;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Handlers;

/// <summary>
/// Thin UserControl wrapper so the TControl type parameter resolves to a type
/// defined in this compilation unit, avoiding cross-project generic constraint
/// resolution issues with the IDE language server.
/// At runtime this is just OAuthLoginControl hosted inside a UserControl shell.
/// </summary>
public sealed class OAuthLoginControlWrapper : UserControl
{
    public OAuthLoginControl Inner { get; }

    public OAuthLoginControlWrapper()
    {
        Inner = new OAuthLoginControl();
        Content = Inner;
    }
}

/// <summary>
/// MAUI handler that bridges <see cref="OAuthLoginView"/> (MAUI) to
/// <see cref="OAuthLoginControl"/> (Avalonia) using
/// <see cref="AvaloniaControlHandler{TVirtualView,TControl}"/> from the package.
/// </summary>
public class OAuthLoginViewHandler : AvaloniaControlHandler<OAuthLoginView, OAuthLoginControlWrapper>
{
    public static new readonly IPropertyMapper<OAuthLoginView, OAuthLoginViewHandler> Mapper =
        new PropertyMapper<OAuthLoginView, OAuthLoginViewHandler>(
            AvaloniaControlHandler<OAuthLoginView, OAuthLoginControlWrapper>.Mapper)
        {
            [nameof(OAuthLoginView.AuthorizationUrl)] = MapAuthorizationUrl,
            [nameof(OAuthLoginView.RedirectUri)]      = MapRedirectUri,
            [nameof(OAuthLoginView.ExpectedState)]    = MapExpectedState,
            [nameof(OAuthLoginView.CodeVerifier)]     = MapCodeVerifier,
        };

    public OAuthLoginViewHandler() : base(Mapper) { }

    // ── Avalonia control creation ────────────────────────────────────────

    protected override OAuthLoginControlWrapper CreateAvaloniaControl()
    {
        Console.WriteLine("[OAuthLoginViewHandler] CreateAvaloniaControl");
        var wrapper = new OAuthLoginControlWrapper();
        wrapper.Inner.AuthCompleted += OnAvaloniaAuthCompleted;
        return wrapper;
    }

    // ── Property mappers ─────────────────────────────────────────────────

    private static void MapAuthorizationUrl(OAuthLoginViewHandler handler, OAuthLoginView view)
    {
        if (handler.AvaloniaControl?.Inner is { } ctrl)
            ctrl.AuthorizationUrl = view.AuthorizationUrl;
    }

    private static void MapRedirectUri(OAuthLoginViewHandler handler, OAuthLoginView view)
    {
        if (handler.AvaloniaControl?.Inner is { } ctrl)
            ctrl.RedirectUri = view.RedirectUri;
    }

    private static void MapExpectedState(OAuthLoginViewHandler handler, OAuthLoginView view)
    {
        if (handler.AvaloniaControl?.Inner is { } ctrl)
            ctrl.ExpectedState = view.ExpectedState;
    }

    private static void MapCodeVerifier(OAuthLoginViewHandler handler, OAuthLoginView view)
    {
        if (handler.AvaloniaControl?.Inner is { } ctrl)
            ctrl.CodeVerifier = view.CodeVerifier;
    }

    // ── Avalonia → MAUI event bridge ─────────────────────────────────────

    private void OnAvaloniaAuthCompleted(object? sender, OAuthResult result)
    {
        Console.WriteLine($"[OAuthLoginViewHandler] AuthCompleted: success={result.Success}, error={result.Error}");

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (VirtualView is not OAuthLoginView view) return;

            if (result.Success)
            {
                try
                {
                    var oauthService = GetOAuthService();
                    if (oauthService != null)
                    {
                        var tokens = await oauthService.ExchangeCodeForTokenAsync(result.Code!, result.CodeVerifier!);
                        if (tokens != null && !string.IsNullOrEmpty(tokens.AccessToken))
                        {
                            ApiClientService.Instance.AuthToken = tokens.AccessToken;
                            Console.WriteLine("[OAuthLoginViewHandler] Token exchange OK → //main");
                            view.RaiseAuthCompleted(new OAuthLoginResult { Success = true });
                            await Shell.Current.GoToAsync("//main");
                            return;
                        }
                    }
                    view.RaiseAuthCompleted(new OAuthLoginResult { Success = false, Error = "token_exchange_failed" });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[OAuthLoginViewHandler] Token exchange error: {ex.Message}");
                    view.RaiseAuthCompleted(new OAuthLoginResult { Success = false, Error = ex.Message });
                }
            }
            else
            {
                view.RaiseAuthCompleted(new OAuthLoginResult { Success = false, Error = result.Error });
            }
        });
    }

    private static OAuthService? GetOAuthService()
    {
        try
        {
            var field = typeof(AuthenticationManager).GetField("_oauthService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(AuthenticationManager.Instance) as OAuthService;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OAuthLoginViewHandler] Could not get OAuthService: {ex.Message}");
            return null;
        }
    }
}
