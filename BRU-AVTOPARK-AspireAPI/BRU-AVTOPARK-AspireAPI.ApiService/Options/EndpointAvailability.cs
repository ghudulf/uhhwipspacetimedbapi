namespace TicketSalesApp.AdminServer.Configuration;

public partial class FeatureFlagOptions
{
    // Opt-in only: distinguish historic implementation selection from intentional availability overrides.
    public bool LegacyAvailabilityCompatibility { get; set; }
    // Null means unspecified, allowing a deliberate compatibility fallback. New installs default enabled.
    public bool? LoginEndpointEnabled { get; set; }
    public bool? RegisterEndpointEnabled { get; set; }
    public bool? TotpSetupEndpointEnabled { get; set; }
    public bool? TotpVerifyEndpointEnabled { get; set; }
    public bool? TotpDisableEndpointEnabled { get; set; }
    public bool? TotpValidateEndpointEnabled { get; set; }
    public bool? WebAuthnRegisterOptionsEndpointEnabled { get; set; }
    public bool? WebAuthnRegisterCompleteEndpointEnabled { get; set; }
    public bool? WebAuthnLoginOptionsEndpointEnabled { get; set; }
    public bool? WebAuthnLoginCompleteEndpointEnabled { get; set; }
    public bool? WebAuthnValidateEndpointEnabled { get; set; }
    public bool? WebAuthnCredentialsEndpointEnabled { get; set; }
    public bool? WebAuthnCredentialDeleteEndpointEnabled { get; set; }
    public bool? MagicLinkSendEndpointEnabled { get; set; }
    public bool? MagicLinkValidateEndpointEnabled { get; set; }
    public bool? MagicLinkPageEndpointEnabled { get; set; }
    public bool? QRLoginPageEndpointEnabled { get; set; }
    public bool? QRLoginGenerateEndpointEnabled { get; set; }
    public bool? QRLoginValidateEndpointEnabled { get; set; }
    public bool? QRLoginDirectEndpointEnabled { get; set; }
    public bool? QRLoginStatusEndpointEnabled { get; set; }
    public bool? QRLoginCancelEndpointEnabled { get; set; }
    public bool? QRLoginNotifyEndpointEnabled { get; set; }
    public bool? OAuthAuthorizeEndpointEnabled { get; set; }
    public bool? OAuthTokenEndpointEnabled { get; set; }
    public bool? OAuthUserInfoEndpointEnabled { get; set; }
    public bool? OAuthTokenInfoEndpointEnabled { get; set; }
    public bool? OAuthAuthorizeCallbackEndpointEnabled { get; set; }
    public bool? OAuthConsentEndpointEnabled { get; set; }
    public bool? OAuthClientRegisterEndpointEnabled { get; set; }
    public bool? OAuthClientListEndpointEnabled { get; set; }
    public bool? OAuthClientDetailsEndpointEnabled { get; set; }
    public bool? OAuthClientUpdateEndpointEnabled { get; set; }
    public bool? OAuthClientDeleteEndpointEnabled { get; set; }
    public bool? OAuthScopesEndpointEnabled { get; set; }
    public bool? OAuthClientRegenerateSecretEndpointEnabled { get; set; }
    public bool? OAuthClientsPageEndpointEnabled { get; set; }
    public bool? OAuthClientNewPageEndpointEnabled { get; set; }
    public bool? OAuthClientDetailsPageEndpointEnabled { get; set; }
    public bool? OAuthClientEditPageEndpointEnabled { get; set; }
    public bool? OAuthScopesPageEndpointEnabled { get; set; }
    public bool? OAuthAuthorizationsPageEndpointEnabled { get; set; }
    public bool? OAuthTokensPageEndpointEnabled { get; set; }
    public bool? OAuthDashboardPageEndpointEnabled { get; set; }
    public bool? OAuthSettingsPageEndpointEnabled { get; set; }
    public bool? OAuthLogsPageEndpointEnabled { get; set; }
    public bool? OAuthHelpPageEndpointEnabled { get; set; }
    public bool? OAuthTestPageEndpointEnabled { get; set; }
    public bool? OAuthCallbackPageEndpointEnabled { get; set; }
    public bool? ProfileEndpointEnabled { get; set; }
    public bool? ProfileUpdateEndpointEnabled { get; set; }
    public bool? ChangePasswordEndpointEnabled { get; set; }
    public bool? LogoutEndpointEnabled { get; set; }
    public bool? RefreshTokenEndpointEnabled { get; set; }
    public bool? SettingsEndpointEnabled { get; set; }
    public bool? SettingsUpdateEndpointEnabled { get; set; }
    public bool? StatusEndpointEnabled { get; set; }
    public bool? WebSocketAuthEndpointEnabled { get; set; }
    public bool? DebugTokenTestEndpointEnabled { get; set; }
    public bool? SuccessPageEndpointEnabled { get; set; }
    public bool? ErrorPageEndpointEnabled { get; set; }
    public bool? ClaimAccountPageEndpointEnabled { get; set; }
    public bool? WebAuthnRegisterPageEndpointEnabled { get; set; }
    public bool? OAuthBackchannelAuthorizeEndpointEnabled { get; set; }
}

public static class EndpointAvailability
{
    private sealed record Setting(string LegacyName, Func<FeatureFlagOptions, bool?> ExplicitValue, Func<FeatureFlagOptions, bool> LegacyValue);
    private static readonly IReadOnlyDictionary<string, Setting> Settings = new Dictionary<string, Setting>(StringComparer.Ordinal)
    {
        [nameof(FeatureFlagOptions.LoginEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableLoginRefactoring), o => o.LoginEndpointEnabled, o => o.EnableLoginRefactoring),
        [nameof(FeatureFlagOptions.RegisterEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableRegisterRefactoring), o => o.RegisterEndpointEnabled, o => o.EnableRegisterRefactoring),
        [nameof(FeatureFlagOptions.TotpSetupEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableTotpSetupRefactoring), o => o.TotpSetupEndpointEnabled, o => o.EnableTotpSetupRefactoring),
        [nameof(FeatureFlagOptions.TotpVerifyEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableTotpVerifyRefactoring), o => o.TotpVerifyEndpointEnabled, o => o.EnableTotpVerifyRefactoring),
        [nameof(FeatureFlagOptions.TotpDisableEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableTotpDisableRefactoring), o => o.TotpDisableEndpointEnabled, o => o.EnableTotpDisableRefactoring),
        [nameof(FeatureFlagOptions.TotpValidateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableTotpValidateRefactoring), o => o.TotpValidateEndpointEnabled, o => o.EnableTotpValidateRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnRegisterOptionsEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnRegisterOptionsRefactoring), o => o.WebAuthnRegisterOptionsEndpointEnabled, o => o.EnableWebAuthnRegisterOptionsRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnRegisterCompleteEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnRegisterCompleteRefactoring), o => o.WebAuthnRegisterCompleteEndpointEnabled, o => o.EnableWebAuthnRegisterCompleteRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnLoginOptionsEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnLoginOptionsRefactoring), o => o.WebAuthnLoginOptionsEndpointEnabled, o => o.EnableWebAuthnLoginOptionsRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnLoginCompleteEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnLoginCompleteRefactoring), o => o.WebAuthnLoginCompleteEndpointEnabled, o => o.EnableWebAuthnLoginCompleteRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnValidateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnValidateRefactoring), o => o.WebAuthnValidateEndpointEnabled, o => o.EnableWebAuthnValidateRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnCredentialsEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnCredentialsRefactoring), o => o.WebAuthnCredentialsEndpointEnabled, o => o.EnableWebAuthnCredentialsRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnCredentialDeleteEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnCredentialDeleteRefactoring), o => o.WebAuthnCredentialDeleteEndpointEnabled, o => o.EnableWebAuthnCredentialDeleteRefactoring),
        [nameof(FeatureFlagOptions.MagicLinkSendEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableMagicLinkSendRefactoring), o => o.MagicLinkSendEndpointEnabled, o => o.EnableMagicLinkSendRefactoring),
        [nameof(FeatureFlagOptions.MagicLinkValidateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableMagicLinkValidateRefactoring), o => o.MagicLinkValidateEndpointEnabled, o => o.EnableMagicLinkValidateRefactoring),
        [nameof(FeatureFlagOptions.MagicLinkPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableMagicLinkPageRefactoring), o => o.MagicLinkPageEndpointEnabled, o => o.EnableMagicLinkPageRefactoring),
        [nameof(FeatureFlagOptions.QRLoginPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginPageRefactoring), o => o.QRLoginPageEndpointEnabled, o => o.EnableQRLoginPageRefactoring),
        [nameof(FeatureFlagOptions.QRLoginGenerateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginGenerateRefactoring), o => o.QRLoginGenerateEndpointEnabled, o => o.EnableQRLoginGenerateRefactoring),
        [nameof(FeatureFlagOptions.QRLoginValidateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginValidateRefactoring), o => o.QRLoginValidateEndpointEnabled, o => o.EnableQRLoginValidateRefactoring),
        [nameof(FeatureFlagOptions.QRLoginDirectEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginDirectRefactoring), o => o.QRLoginDirectEndpointEnabled, o => o.EnableQRLoginDirectRefactoring),
        [nameof(FeatureFlagOptions.QRLoginStatusEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginStatusRefactoring), o => o.QRLoginStatusEndpointEnabled, o => o.EnableQRLoginStatusRefactoring),
        [nameof(FeatureFlagOptions.QRLoginCancelEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginCancelRefactoring), o => o.QRLoginCancelEndpointEnabled, o => o.EnableQRLoginCancelRefactoring),
        [nameof(FeatureFlagOptions.QRLoginNotifyEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableQRLoginNotifyRefactoring), o => o.QRLoginNotifyEndpointEnabled, o => o.EnableQRLoginNotifyRefactoring),
        [nameof(FeatureFlagOptions.OAuthAuthorizeEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthAuthorizeRefactoring), o => o.OAuthAuthorizeEndpointEnabled, o => o.EnableOAuthAuthorizeRefactoring),
        [nameof(FeatureFlagOptions.OAuthTokenEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthTokenRefactoring), o => o.OAuthTokenEndpointEnabled, o => o.EnableOAuthTokenRefactoring),
        [nameof(FeatureFlagOptions.OAuthUserInfoEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthUserInfoRefactoring), o => o.OAuthUserInfoEndpointEnabled, o => o.EnableOAuthUserInfoRefactoring),
        [nameof(FeatureFlagOptions.OAuthTokenInfoEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthTokenInfoRefactoring), o => o.OAuthTokenInfoEndpointEnabled, o => o.EnableOAuthTokenInfoRefactoring),
        [nameof(FeatureFlagOptions.OAuthAuthorizeCallbackEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthAuthorizeCallbackRefactoring), o => o.OAuthAuthorizeCallbackEndpointEnabled, o => o.EnableOAuthAuthorizeCallbackRefactoring),
        [nameof(FeatureFlagOptions.OAuthConsentEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthConsentRefactoring), o => o.OAuthConsentEndpointEnabled, o => o.EnableOAuthConsentRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientRegisterEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientRegisterRefactoring), o => o.OAuthClientRegisterEndpointEnabled, o => o.EnableOAuthClientRegisterRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientListEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientListRefactoring), o => o.OAuthClientListEndpointEnabled, o => o.EnableOAuthClientListRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientDetailsEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientDetailsRefactoring), o => o.OAuthClientDetailsEndpointEnabled, o => o.EnableOAuthClientDetailsRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientUpdateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientUpdateRefactoring), o => o.OAuthClientUpdateEndpointEnabled, o => o.EnableOAuthClientUpdateRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientDeleteEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientDeleteRefactoring), o => o.OAuthClientDeleteEndpointEnabled, o => o.EnableOAuthClientDeleteRefactoring),
        [nameof(FeatureFlagOptions.OAuthScopesEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthScopesRefactoring), o => o.OAuthScopesEndpointEnabled, o => o.EnableOAuthScopesRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientRegenerateSecretEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientRegenerateSecretRefactoring), o => o.OAuthClientRegenerateSecretEndpointEnabled, o => o.EnableOAuthClientRegenerateSecretRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientsPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientsPageRefactoring), o => o.OAuthClientsPageEndpointEnabled, o => o.EnableOAuthClientsPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientNewPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientNewPageRefactoring), o => o.OAuthClientNewPageEndpointEnabled, o => o.EnableOAuthClientNewPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientDetailsPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientDetailsPageRefactoring), o => o.OAuthClientDetailsPageEndpointEnabled, o => o.EnableOAuthClientDetailsPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthClientEditPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthClientEditPageRefactoring), o => o.OAuthClientEditPageEndpointEnabled, o => o.EnableOAuthClientEditPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthScopesPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthScopesPageRefactoring), o => o.OAuthScopesPageEndpointEnabled, o => o.EnableOAuthScopesPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthAuthorizationsPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthAuthorizationsPageRefactoring), o => o.OAuthAuthorizationsPageEndpointEnabled, o => o.EnableOAuthAuthorizationsPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthTokensPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthTokensPageRefactoring), o => o.OAuthTokensPageEndpointEnabled, o => o.EnableOAuthTokensPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthDashboardPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthDashboardPageRefactoring), o => o.OAuthDashboardPageEndpointEnabled, o => o.EnableOAuthDashboardPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthSettingsPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthSettingsPageRefactoring), o => o.OAuthSettingsPageEndpointEnabled, o => o.EnableOAuthSettingsPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthLogsPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthLogsPageRefactoring), o => o.OAuthLogsPageEndpointEnabled, o => o.EnableOAuthLogsPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthHelpPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthHelpPageRefactoring), o => o.OAuthHelpPageEndpointEnabled, o => o.EnableOAuthHelpPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthTestPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthTestPageRefactoring), o => o.OAuthTestPageEndpointEnabled, o => o.EnableOAuthTestPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthCallbackPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthCallbackPageRefactoring), o => o.OAuthCallbackPageEndpointEnabled, o => o.EnableOAuthCallbackPageRefactoring),
        [nameof(FeatureFlagOptions.ProfileEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableProfileRefactoring), o => o.ProfileEndpointEnabled, o => o.EnableProfileRefactoring),
        [nameof(FeatureFlagOptions.ProfileUpdateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableProfileUpdateRefactoring), o => o.ProfileUpdateEndpointEnabled, o => o.EnableProfileUpdateRefactoring),
        [nameof(FeatureFlagOptions.ChangePasswordEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableChangePasswordRefactoring), o => o.ChangePasswordEndpointEnabled, o => o.EnableChangePasswordRefactoring),
        [nameof(FeatureFlagOptions.LogoutEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableLogoutRefactoring), o => o.LogoutEndpointEnabled, o => o.EnableLogoutRefactoring),
        [nameof(FeatureFlagOptions.RefreshTokenEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableRefreshTokenRefactoring), o => o.RefreshTokenEndpointEnabled, o => o.EnableRefreshTokenRefactoring),
        [nameof(FeatureFlagOptions.SettingsEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableSettingsRefactoring), o => o.SettingsEndpointEnabled, o => o.EnableSettingsRefactoring),
        [nameof(FeatureFlagOptions.SettingsUpdateEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableSettingsUpdateRefactoring), o => o.SettingsUpdateEndpointEnabled, o => o.EnableSettingsUpdateRefactoring),
        [nameof(FeatureFlagOptions.StatusEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableStatusRefactoring), o => o.StatusEndpointEnabled, o => o.EnableStatusRefactoring),
        [nameof(FeatureFlagOptions.WebSocketAuthEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebSocketAuthRefactoring), o => o.WebSocketAuthEndpointEnabled, o => o.EnableWebSocketAuthRefactoring),
        [nameof(FeatureFlagOptions.DebugTokenTestEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableDebugTokenTestRefactoring), o => o.DebugTokenTestEndpointEnabled, o => o.EnableDebugTokenTestRefactoring),
        [nameof(FeatureFlagOptions.SuccessPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableSuccessPageRefactoring), o => o.SuccessPageEndpointEnabled, o => o.EnableSuccessPageRefactoring),
        [nameof(FeatureFlagOptions.ErrorPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableErrorPageRefactoring), o => o.ErrorPageEndpointEnabled, o => o.EnableErrorPageRefactoring),
        [nameof(FeatureFlagOptions.ClaimAccountPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableClaimAccountPageRefactoring), o => o.ClaimAccountPageEndpointEnabled, o => o.EnableClaimAccountPageRefactoring),
        [nameof(FeatureFlagOptions.WebAuthnRegisterPageEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableWebAuthnRegisterPageRefactoring), o => o.WebAuthnRegisterPageEndpointEnabled, o => o.EnableWebAuthnRegisterPageRefactoring),
        [nameof(FeatureFlagOptions.OAuthBackchannelAuthorizeEndpointEnabled)] = new(nameof(FeatureFlagOptions.EnableOAuthBackchannelAuthorizeRefactoring), o => o.OAuthBackchannelAuthorizeEndpointEnabled, o => o.EnableOAuthBackchannelAuthorizeRefactoring),
    };
    private static readonly IReadOnlyDictionary<string, string> LegacyNames = Settings.ToDictionary(p => p.Value.LegacyName, p => p.Key, StringComparer.Ordinal);
    public static string CanonicalName(string legacyName) => LegacyNames.GetValueOrDefault(legacyName) ?? legacyName;
    public static bool IsAvailabilityFlag(string name) => Settings.ContainsKey(name);
    public static bool Resolve(FeatureFlagOptions options, string name, Func<string, bool?> committedOverride)
    {
        if (!Settings.TryGetValue(name, out var setting)) return false;
        return committedOverride(name) ?? setting.ExplicitValue(options)
            ?? (options.LegacyAvailabilityCompatibility
                ? committedOverride(setting.LegacyName) ?? setting.LegacyValue(options)
                : true);
    }
}
