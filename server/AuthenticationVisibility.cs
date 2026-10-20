#pragma warning disable STDB_UNSTABLE // Pinned SDK 2.1.0 RLS API, verified during publish.
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter UserProfileApiAccess = new Filter.Sql("SELECT UserProfile.* FROM UserProfile JOIN TrustedApiIdentity ON UserProfile.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter UserSettingsApiAccess = new Filter.Sql("SELECT UserSettings.* FROM UserSettings JOIN TrustedApiIdentity ON UserSettings.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter QRSessionApiAccess = new Filter.Sql("SELECT QRSession.* FROM QRSession JOIN TrustedApiIdentity ON QRSession.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter TotpSecretApiAccess = new Filter.Sql("SELECT TotpSecret.* FROM TotpSecret JOIN TrustedApiIdentity ON TotpSecret.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter TwoFactorTokenApiAccess = new Filter.Sql("SELECT TwoFactorToken.* FROM TwoFactorToken JOIN TrustedApiIdentity ON TwoFactorToken.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter WebAuthnCredentialApiAccess = new Filter.Sql("SELECT WebAuthnCredential.* FROM WebAuthnCredential JOIN TrustedApiIdentity ON WebAuthnCredential.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter WebAuthnChallengeApiAccess = new Filter.Sql("SELECT WebAuthnChallenge.* FROM WebAuthnChallenge JOIN TrustedApiIdentity ON WebAuthnChallenge.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter MagicLinkTokenApiAccess = new Filter.Sql("SELECT MagicLinkToken.* FROM MagicLinkToken JOIN TrustedApiIdentity ON MagicLinkToken.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter OpenIdConnectApiAccess = new Filter.Sql("SELECT OpenIdConnect.* FROM OpenIdConnect JOIN TrustedApiIdentity ON OpenIdConnect.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter OpenIdConnectGrantApiAccess = new Filter.Sql("SELECT OpenIdConnectGrant.* FROM OpenIdConnectGrant JOIN TrustedApiIdentity ON OpenIdConnectGrant.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter OpenIddictSpacetimeTokenApiAccess = new Filter.Sql("SELECT OpenIddictSpacetimeToken.* FROM OpenIddictSpacetimeToken JOIN TrustedApiIdentity ON OpenIddictSpacetimeToken.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter OpenIddictSpacetimeAuthorizationApiAccess = new Filter.Sql("SELECT OpenIddictSpacetimeAuthorization.* FROM OpenIddictSpacetimeAuthorization JOIN TrustedApiIdentity ON OpenIddictSpacetimeAuthorization.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter UserRoleApiAccess = new Filter.Sql("SELECT UserRole.* FROM UserRole JOIN TrustedApiIdentity ON UserRole.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter RolePermissionApiAccess = new Filter.Sql("SELECT RolePermission.* FROM RolePermission JOIN TrustedApiIdentity ON RolePermission.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

}
