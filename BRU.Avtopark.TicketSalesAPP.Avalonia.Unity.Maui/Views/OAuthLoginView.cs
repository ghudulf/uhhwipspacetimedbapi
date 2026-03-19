using Microsoft.Maui.Controls;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;

/// <summary>
/// MAUI View that wraps the Avalonia <c>OAuthLoginControl</c>.
/// The corresponding <see cref="Handlers.OAuthLoginViewHandler"/> creates the
/// Avalonia control and bridges events back to MAUI Shell navigation.
/// </summary>
public class OAuthLoginView : View
{
    // ── Bindable properties ──────────────────────────────────────────────

    public static readonly BindableProperty AuthorizationUrlProperty =
        BindableProperty.Create(nameof(AuthorizationUrl), typeof(string), typeof(OAuthLoginView), string.Empty);

    public static readonly BindableProperty RedirectUriProperty =
        BindableProperty.Create(nameof(RedirectUri), typeof(string), typeof(OAuthLoginView), string.Empty);

    public static readonly BindableProperty ExpectedStateProperty =
        BindableProperty.Create(nameof(ExpectedState), typeof(string), typeof(OAuthLoginView), string.Empty);

    public static readonly BindableProperty CodeVerifierProperty =
        BindableProperty.Create(nameof(CodeVerifier), typeof(string), typeof(OAuthLoginView), string.Empty);

    public string AuthorizationUrl
    {
        get => (string)GetValue(AuthorizationUrlProperty);
        set => SetValue(AuthorizationUrlProperty, value);
    }

    public string RedirectUri
    {
        get => (string)GetValue(RedirectUriProperty);
        set => SetValue(RedirectUriProperty, value);
    }

    public string ExpectedState
    {
        get => (string)GetValue(ExpectedStateProperty);
        set => SetValue(ExpectedStateProperty, value);
    }

    public string CodeVerifier
    {
        get => (string)GetValue(CodeVerifierProperty);
        set => SetValue(CodeVerifierProperty, value);
    }

    // ── Events ───────────────────────────────────────────────────────────

    /// <summary>Raised when the OAuth flow completes (success or failure).</summary>
    public event EventHandler<OAuthLoginResult>? AuthCompleted;

    internal void RaiseAuthCompleted(OAuthLoginResult result) =>
        AuthCompleted?.Invoke(this, result);
}

/// <summary>Result passed back from the Avalonia OAuth control to the MAUI page.</summary>
public sealed class OAuthLoginResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Code { get; init; }
    public string? CodeVerifier { get; init; }
}
