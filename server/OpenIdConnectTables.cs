using System.Text;
using SpacetimeDB;

public static partial class Module
{
    // ***** Authentication Tables for WebAuthn, TOTP, and Magic Links - obviously compliant with spacetimedb requirements *****
    [SpacetimeDB.Table(Public = true)]
    public partial class TwoFactorToken
    {
        [PrimaryKey]
        public uint Id;
        public Identity UserId;
        public string Token;
        public ulong ExpiresAt; // DATETIME IS ALWAYS INVALID FOR SPACETIMEDB - MUST BE ULONG FOR ANYTHING RELATED TO TIME AND USE UNIX TIMESTAMPS
        public bool IsUsed;
        public string? DeviceInfo;
        public string? IpAddress;
    }


    [SpacetimeDB.Table(Public = true)]
    public partial class TotpSecret
    {
        [PrimaryKey]
        public uint Id; // This is the unique identifier for the TOTP secret YOU CANT CHANGE THIS - SPACETIMEDB DOES NOT ALLOW PRIMARY KEYS TO BE REFERENCES TO OTHER TABLES
        public Identity UserId;
        public string Secret;
        public ulong CreatedAt;
        public bool IsActive;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class WebAuthnCredential
    {
        [PrimaryKey]
        public uint Id;
        public Identity UserId;
        public byte[] CredentialId;
        public string PublicKey;
        public uint Counter;
        public ulong CreatedAt;
        public bool IsActive;
        public string? DeviceName;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class WebAuthnChallenge
    {
        [PrimaryKey]
        public uint Id;
        public Identity UserId;
        public string Challenge;
        public ulong ExpiresAt;
        public ulong CreatedAt;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class MagicLinkToken
    {
        [PrimaryKey]
        public string Token;
        public Identity UserId;
        public ulong ExpiresAt;
        public bool IsUsed;
        public string? DeviceInfo;
        public string? IpAddress;
    }

    // ----- Authorization Store Tables -----
    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIddictSpacetimeAuthorization
    {
        [PrimaryKey]
        public uint Id;

        
        public string OpenIddictAuthorizationId; // OpenIddict's string ID

        public string? ApplicationClientId; // Reference OpenIdConnect.ClientId
        public ulong? CreationDate; // Unix ms
        public string? Properties; // JSON map
        public string? Scopes; // JSON array '["scope1", "scope2"]'
        public string? Status;
        public string? Subject; // User's SpacetimeDB Identity string
        public string? Type; // authorization_code, refresh_token, device_code, user_code
    }

    [SpacetimeDB.Table]
    public partial class OidcAuthorizationIdCounter { [PrimaryKey] public string Key = "oidcAuthId"; public uint NextId = 0; }

    // ----- Token Store Tables -----
    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIddictSpacetimeToken
    {
        [PrimaryKey]
        public uint Id;

       
        public string OpenIddictTokenId; // OpenIddict's string ID

        public uint? AuthorizationId; // Link to OpenIddictSpacetimeAuthorization.Id
        public string? ApplicationClientId; // Link to OpenIdConnect.ClientId
        public ulong? CreationDate; // Unix ms
        public ulong? ExpirationDate; // Unix ms
        public string? Payload; // JWT payload or reference token content
        public string? Properties; // JSON map
        public ulong? RedemptionDate; // Unix ms
        public string? ReferenceId; // For reference tokens
        public string? Status;
        public string? Subject; // User's SpacetimeDB Identity string
        public string? Type; // access_token, refresh_token, authorization_code, identity_token, user_code, device_code
    }

    [SpacetimeDB.Table]
    public partial class OidcTokenIdCounter { [PrimaryKey] public string Key = "oidcTokenId"; public uint NextId = 0; }

    // ----- Scope Store Tables -----
    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIddictSpacetimeScope
    {
        [PrimaryKey]
        public uint Id;

       
        public string OpenIddictScopeId; // OpenIddict's string ID

        
        public string Name; // e.g., "openid", "profile", "api"

        public string? Description;
        public string? Descriptions; // JSON map for localized
        public string? DisplayName;
        public string? DisplayNames; // JSON map for localized
        public string? Properties; // JSON map
        public string? Resources; // JSON array of associated resource identifiers
    }

    [SpacetimeDB.Table]
    public partial class OidcScopeIdCounter { [PrimaryKey] public string Key = "oidcScopeId"; public uint NextId = 0; }

    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIdConnect
    {
        [PrimaryKey]
        public string ClientId;
        public string ClientSecret;
        public string DisplayName;
        public string[] RedirectUris;
        public string[] PostLogoutRedirectUris;
        public string[] AllowedScopes;
        public string ConsentType;  // "explicit" or "implicit" based on requireConsent
        public string ClientType;   // "public"
        public bool IsActive;
        public ulong CreatedAt;
        public string? CreatedBy;
        public bool RequireConsent;  // bool for whether it needs consent to access resources - true or false
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIdConnectGrant
    {
        [PrimaryKey]
        public string GrantId;
        public string ClientId;
        public Identity UserId;
        public string Type;  // "authorization_code", "refresh_token"
        public string[] Scopes;
        public ulong CreatedAt;
        public ulong ExpiresAt;
        public bool IsRevoked;
    }

    // Add new reducers for auth features

    [SpacetimeDB.Reducer]
    public static void CreateUserSettings(ReducerContext ctx, Identity userId)
    {
        var userSettings = new UserSettings
        {
            UserId = userId,
            TotpEnabled = false,
            WebAuthnEnabled = false,
            IsEmailNotificationsEnabled = true,
            IsSmsNotificationsEnabled = true,
            IsPushNotificationsEnabled = true,
            IsWhatsAppNotificationsEnabled = true,
            IsTelegramNotificationsEnabled = true,
            IsDiscordNotificationsEnabled = true
        };
        ctx.Db.UserSettings.Insert(userSettings);
    }

    [SpacetimeDB.Reducer]
    public static void StoreTotpSecret(ReducerContext ctx, Identity userId, string secret)
    {
        var userSettings = ctx.Db.UserSettings.Iter()
            .FirstOrDefault(u => u.UserId == userId);
        if (userSettings == null)
        {
            throw new Exception("User settings not found");
        }
        var totpSecret = new TotpSecret
        {
            Id = GetNextId(ctx, "totpSecretId"),
            UserId = userId,
            Secret = secret,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            IsActive = true
        };
        ctx.Db.TotpSecret.Insert(totpSecret);
    }

    [SpacetimeDB.Reducer]
    public static void EnableTotp(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter()
            .FirstOrDefault(u => u.UserId == userId);
        if (userSettings == null)
        {
            throw new Exception("User settings not found");
        }
        userSettings.TotpEnabled = true;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
    }

    [SpacetimeDB.Reducer]
    public static void DisableTotp(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter()
            .FirstOrDefault(u => u.UserId == userId);
        if (userSettings == null)
        {
            throw new Exception("User settings not found");
        }
        userSettings.TotpEnabled = false;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
    }

    [SpacetimeDB.Reducer]
    public static void EnableWebAuthn(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter()
            .FirstOrDefault(u => u.UserId == userId);
        if (userSettings == null)
        {
            throw new Exception("User settings not found");
        }
        userSettings.WebAuthnEnabled = true;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
    }






    [SpacetimeDB.Reducer]
    public static void CreateTwoFactorToken(ReducerContext ctx, Identity userId, string token, bool isUsed, ulong expiresAt, string? deviceInfo = null, string? ipAddress = null)
    {
        var twoFactorToken = new TwoFactorToken
        {
            Id = GetNextId(ctx, "twoFactorTokenId"),
            UserId = userId,
            Token = token,
            ExpiresAt = expiresAt,
            IsUsed = isUsed,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };
        ctx.Db.TwoFactorToken.Insert(twoFactorToken);
    }

    [SpacetimeDB.Reducer]
    public static void DeleteTwoFactorToken(ReducerContext ctx, uint id)
    {
        var token = ctx.Db.TwoFactorToken.Id.Find(id);
        if (token == null)
        {
            throw new Exception("Two-factor token not found");
        }
        ctx.Db.TwoFactorToken.Id.Delete(id);
    }

    [SpacetimeDB.Reducer]
    public static void GenerateTotpSecret(ReducerContext ctx, Identity userId, string secret)
    {
        var totpSecret = new TotpSecret
        {
            Id = GetNextId(ctx, "totpSecretId"),
            UserId = userId,
            Secret = secret,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            IsActive = true
        };
        ctx.Db.TotpSecret.Insert(totpSecret);
    }

    [SpacetimeDB.Reducer]
    public static void RegisterWebAuthnCredential(ReducerContext ctx, Identity userId, byte[] credentialId, string publicKey, uint counter, string? deviceName = null)
    {
        var webAuthnCredential = new WebAuthnCredential
        {
            Id = GetNextId(ctx, "webAuthnCredentialId"),
            UserId = userId,
            CredentialId = credentialId,
            PublicKey = publicKey,
            Counter = counter,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            IsActive = true,
            DeviceName = deviceName
        };
        ctx.Db.WebAuthnCredential.Insert(webAuthnCredential);
    }

    [SpacetimeDB.Reducer]
    public static void DeactivateWebAuthnCredential(ReducerContext ctx, uint id)
    {
        var credential = ctx.Db.WebAuthnCredential.Id.Find(id);
        if (credential == null)
        {
            throw new Exception("WebAuthn credential not found");
        }
        credential.IsActive = false;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
    }


    [SpacetimeDB.Reducer]
    public static void CreateMagicLinkToken(ReducerContext ctx, Identity userId, string token, ulong expiresAt, string? deviceInfo = null, string? ipAddress = null)
    {
        var magicLinkToken = new MagicLinkToken
        {
            Token = token,
            UserId = userId,
            ExpiresAt = expiresAt,
            IsUsed = false,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };
        ctx.Db.MagicLinkToken.Insert(magicLinkToken);
    }



    [SpacetimeDB.Reducer]
    public static void RegisterOpenIdClient(ReducerContext ctx, string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, string consentType, string clientType)
    {
        var openIdConnect = new OpenIdConnect
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = displayName,
            RedirectUris = redirectUris,
            PostLogoutRedirectUris = postLogoutRedirectUris,
            AllowedScopes = allowedScopes,
            ConsentType = consentType,
            ClientType = clientType,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            CreatedBy = ctx.Identity.ToString(),
            RequireConsent = consentType == "explicit" // If consentType is "explicit", RequireConsent is true, otherwise false
        };
        ctx.Db.OpenIdConnect.Insert(openIdConnect);
    }

    [SpacetimeDB.Reducer]
    public static void UpdateOpenIdClient(ReducerContext ctx, string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, string consentType)
    {
        var openIdConnect = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        if (openIdConnect == null)
        {
            throw new Exception("OpenID Connect client not found");
        }
        openIdConnect.ClientSecret = clientSecret;
        openIdConnect.DisplayName = displayName;
        openIdConnect.RedirectUris = redirectUris;
        openIdConnect.PostLogoutRedirectUris = postLogoutRedirectUris;
        openIdConnect.AllowedScopes = allowedScopes;
        openIdConnect.ConsentType = consentType;
        openIdConnect.RequireConsent = consentType == "explicit";

        ctx.Db.OpenIdConnect.ClientId.Update(openIdConnect);
    }

    [SpacetimeDB.Reducer]
    public static void RevokeOpenIdClient(ReducerContext ctx, string clientId)
    {
        var openIdConnect = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        if (openIdConnect == null)
        {
            throw new Exception("OpenID Connect client not found");
        }
        openIdConnect.IsActive = false;
        ctx.Db.OpenIdConnect.ClientId.Update(openIdConnect);
    }


    [SpacetimeDB.Reducer]
    public static void CreateOpenIdGrant(ReducerContext ctx, string grantId, string clientId, Identity userId, string type, string[] scopes, ulong expiresAt)
    {
        var openIdConnectGrant = new OpenIdConnectGrant
        {
            GrantId = grantId,
            ClientId = clientId,
            UserId = userId,
            Type = type,
            Scopes = scopes,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            ExpiresAt = expiresAt,
            IsRevoked = false
        };
        ctx.Db.OpenIdConnectGrant.Insert(openIdConnectGrant);
    }

    [SpacetimeDB.Reducer]
    public static void RevokeOpenIdGrant(ReducerContext ctx, string grantId)
    {
        var grant = ctx.Db.OpenIdConnectGrant.GrantId.Find(grantId);
        if (grant != null)
        {
            grant.IsRevoked = true;
            ctx.Db.OpenIdConnectGrant.GrantId.Update(grant);
        }
    }

    [SpacetimeDB.Reducer]
    public static void UpdateWebAuthnCounter(ReducerContext ctx, byte[] credentialId, uint counter)
    {
        var credential = ctx.Db.WebAuthnCredential.Iter()
            .FirstOrDefault(c => c.CredentialId.SequenceEqual(credentialId));

        if (credential == null)
        {
            throw new Exception("WebAuthn credential not found");
        }

        credential.Counter = counter;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
    }

    [SpacetimeDB.Reducer]
    public static void ApproveQrSession(ReducerContext ctx, string sessionId)
    {
        Log.Info($"Approving QR session {sessionId}");

        // Validate input
        if (string.IsNullOrEmpty(sessionId))
        {
            Log.Error("Session ID cannot be empty");
            return;
        }

        // Find QR session
        var session = ctx.Db.QRSession.Iter()
            .FirstOrDefault(s => s.SessionId == sessionId);
        if (session == null)
        {
            Log.Error($"QR session {sessionId} not found");
            return;
        }

        // Check if session is expired
        var currentTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (session.ExpiryTime < currentTime)
        {
            Log.Error($"QR session {sessionId} has expired");
            return;
        }

        // Check if session is already used
        if (session.IsUsed)
        {
            Log.Error($"QR session {sessionId} has already been used");
            return;
        }

        // Update session
        session.IsUsed = true;
        ctx.Db.QRSession.SessionId.Update(session);
    }


    [SpacetimeDB.Reducer]
    public static void UseMagicLinkToken(ReducerContext ctx, string token)
    {
        var magicLinkToken = ctx.Db.MagicLinkToken.Token.Find(token);
        if (magicLinkToken == null)
        {
            throw new Exception("Magic link token not found");
        }

        magicLinkToken.IsUsed = true;
        ctx.Db.MagicLinkToken.Token.Update(magicLinkToken);
    }


    [SpacetimeDB.Reducer]
    public static void StoreWebAuthnChallenge(ReducerContext ctx, Identity userId, string challenge, ulong expiresAt)
    {
        var webAuthnChallenge = new WebAuthnChallenge
        {
            Id = GetNextId(ctx, "webAuthnChallengeId"),
            UserId = userId,
            Challenge = challenge,
            ExpiresAt = expiresAt,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        ctx.Db.WebAuthnChallenge.Insert(webAuthnChallenge);
    }

    [SpacetimeDB.Reducer]
    public static void DeleteWebAuthnChallenge(ReducerContext ctx, uint id)
    {
        var challenge = ctx.Db.WebAuthnChallenge.Id.Find(id);
        if (challenge == null)
        {
            throw new Exception("WebAuthn challenge not found");
        }
        ctx.Db.WebAuthnChallenge.Id.Delete(id);
    }

    [SpacetimeDB.Reducer]
    public static void UpdateWebAuthnCredentialCounter(ReducerContext ctx, uint id, uint counter)
    {
        var credential = ctx.Db.WebAuthnCredential.Id.Find(id);
        if (credential == null)
        {
            throw new Exception("WebAuthn credential not found");
        }
        credential.Counter = counter;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
    }

    [SpacetimeDB.Reducer]
    public static void DeactivateTotpSecret(ReducerContext ctx, Identity userId)
    {
        var totpSecret = ctx.Db.TotpSecret.Iter()
            .FirstOrDefault(t => t.UserId == userId);
        if (totpSecret == null)
        {
            throw new Exception("TOTP secret not found");
        }
        totpSecret.IsActive = false;
        ctx.Db.TotpSecret.Id.Update(totpSecret);
    }

    [SpacetimeDB.Reducer]
    public static void DisableWebAuthn(ReducerContext ctx, Identity userId)
    {
        //should disable webauthn in usersettings and purge credentials at same time - so first modify user settings then delete stored webauthn creds
        /*so reverse of

        var userSettings = ctx.Db.UserSettings.Iter()
                    .FirstOrDefault(u => u.UserId == userId);
                if (userSettings == null)
                {
                    throw new Exception("User settings not found");
                }
                userSettings.WebAuthnEnabled = true;
                ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        */
        var userSettings = ctx.Db.UserSettings.Iter()
            .FirstOrDefault(u => u.UserId == userId);
        if (userSettings == null)
        {
            throw new Exception("User settings not found");
        }
        userSettings.WebAuthnEnabled = false;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        var webAuthnCredential = ctx.Db.WebAuthnCredential.Iter()
            .FirstOrDefault(w => w.UserId == userId);
        if (webAuthnCredential == null)
        {
            throw new Exception("WebAuthn credential not found");
        }
        webAuthnCredential.IsActive = false;
        ctx.Db.WebAuthnCredential.Id.Update(webAuthnCredential);

    }

    [SpacetimeDB.Reducer]
    public static void UpdateTwoFactorToken(ReducerContext ctx, uint id, Identity userId, string token, bool isUsed, ulong expiresAt)
    {
        var twoFactorToken = ctx.Db.TwoFactorToken.Id.Find(id);
        if (twoFactorToken == null)
        {
            throw new Exception("Two-factor token not found");
        }
        twoFactorToken.UserId = userId;
        twoFactorToken.Token = token;
        twoFactorToken.IsUsed = isUsed;
        twoFactorToken.ExpiresAt = expiresAt;
        ctx.Db.TwoFactorToken.Id.Update(twoFactorToken);
    }

    // --- Authorization Reducers ---
    [SpacetimeDB.Reducer]
    public static void CreateOidcAuthorization(ReducerContext ctx, string oidcAuthId, string? appClientId, ulong? creationDate, string? propertiesJson, string? scopesJson, string? status, string? subject, string? type)
    {
        if (ctx.Db.OpenIddictSpacetimeAuthorization.Iter().Any(a => a.OpenIddictAuthorizationId == oidcAuthId)) {
            Log.Error($"Reducer: OIDC Authorization with OIDC ID {oidcAuthId} already exists.");
            return; // Or throw
        }
        uint internalId = GetNextId(ctx, "oidcAuthId");
        var newAuth = new OpenIddictSpacetimeAuthorization {
            Id = internalId, OpenIddictAuthorizationId = oidcAuthId, ApplicationClientId = appClientId,
            CreationDate = creationDate, Properties = propertiesJson, Scopes = scopesJson,
            Status = status, Subject = subject, Type = type
        };
        ctx.Db.OpenIddictSpacetimeAuthorization.Insert(newAuth);
        Log.Info($"Reducer: Created OIDC Authorization {oidcAuthId} (Internal ID: {internalId})");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteOidcAuthorization(ReducerContext ctx, uint internalId)
    {
        var auth = ctx.Db.OpenIddictSpacetimeAuthorization.Id.Find(internalId);
        if (auth != null) {
             ctx.Db.OpenIddictSpacetimeAuthorization.Id.Delete(internalId);
             Log.Info($"Reducer: Deleted OIDC Authorization internal ID {internalId} (OIDC ID: {auth.OpenIddictAuthorizationId})");
             // Cascade delete associated tokens
             var tokensToDelete = ctx.Db.OpenIddictSpacetimeToken.Iter().Where(t => t.AuthorizationId == internalId).ToList();
             foreach(var token in tokensToDelete) {
                  ctx.Db.OpenIddictSpacetimeToken.Id.Delete(token.Id);
                  Log.Debug($"Reducer: Deleted associated token internal ID {token.Id}");
             }
        } else {
             Log.Warn($"Reducer: Could not delete OIDC Authorization, internal ID {internalId} not found.");
        }
    }

    [SpacetimeDB.Reducer]
    public static void UpdateOidcAuthorization(ReducerContext ctx, uint internalId, string? propertiesJson, string? scopesJson, string? status)
    {
        var auth = ctx.Db.OpenIddictSpacetimeAuthorization.Id.Find(internalId);
         if (auth != null) {
             if(propertiesJson != null) auth.Properties = propertiesJson;
             if(scopesJson != null) auth.Scopes = scopesJson;
             if(status != null) auth.Status = status;
             ctx.Db.OpenIddictSpacetimeAuthorization.Id.Update(auth);
             Log.Info($"Reducer: Updated OIDC Authorization internal ID {internalId}");
         } else {
              Log.Warn($"Reducer: Could not update OIDC Authorization, internal ID {internalId} not found.");
         }
    }

    [SpacetimeDB.Reducer]
    public static void PruneOidcAuthorizations(ReducerContext ctx, ulong thresholdDate)
    {
         Log.Info($"Reducer: Pruning OIDC authorizations created before {thresholdDate}");
         // Find authorizations that are inactive/revoked and older than the threshold
         var authorizationsToPrune = ctx.Db.OpenIddictSpacetimeAuthorization.Iter()
             .Where(auth => (auth.Status == "inactive" || auth.Status == "revoked") &&
                             auth.CreationDate.HasValue && auth.CreationDate < thresholdDate)
             .ToList();

         int count = 0;
         foreach (var auth in authorizationsToPrune)
         {
              DeleteOidcAuthorization(ctx, auth.Id); // Reuse delete reducer for cascade
              count++;
         }
         Log.Info($"Reducer: Pruned {count} OIDC authorizations.");
    }

    // --- Token Reducers ---
    [SpacetimeDB.Reducer]
    public static void CreateOidcToken(ReducerContext ctx, string oidcTokenId, uint? authInternalId, string? appClientId, ulong? creationDate, ulong? expirationDate, string? payload, string? propertiesJson, ulong? redemptionDate, string? referenceId, string? status, string? subject, string? type)
    {
         if (ctx.Db.OpenIddictSpacetimeToken.Iter().Any(t => t.OpenIddictTokenId == oidcTokenId)) {
            Log.Error($"Reducer: OIDC Token with OIDC ID {oidcTokenId} already exists.");
            return; // Or throw
        }
        uint internalId = GetNextId(ctx, "oidcTokenId");
        var newToken = new OpenIddictSpacetimeToken {
            Id = internalId, OpenIddictTokenId = oidcTokenId, AuthorizationId = authInternalId, ApplicationClientId = appClientId,
            CreationDate = creationDate, ExpirationDate = expirationDate, Payload = payload, Properties = propertiesJson,
            RedemptionDate = redemptionDate, ReferenceId = referenceId, Status = status, Subject = subject, Type = type
        };
        ctx.Db.OpenIddictSpacetimeToken.Insert(newToken);
        Log.Info($"Reducer: Created OIDC Token {oidcTokenId} (Internal ID: {internalId}, Type: {type})");
    }

     [SpacetimeDB.Reducer]
    public static void DeleteOidcToken(ReducerContext ctx, uint internalId)
    {
        var token = ctx.Db.OpenIddictSpacetimeToken.Id.Find(internalId);
        if (token != null) {
            ctx.Db.OpenIddictSpacetimeToken.Id.Delete(internalId);
            Log.Info($"Reducer: Deleted OIDC Token internal ID {internalId} (OIDC ID: {token.OpenIddictTokenId})");
        } else {
            Log.Warn($"Reducer: Could not delete OIDC Token, internal ID {internalId} not found.");
        }
    }

     [SpacetimeDB.Reducer]
     public static void UpdateOidcToken(ReducerContext ctx, uint internalId, ulong? expirationDate, string? payload, string? propertiesJson, ulong? redemptionDate, string? status)
     {
          var token = ctx.Db.OpenIddictSpacetimeToken.Id.Find(internalId);
          if (token != null) {
               if(expirationDate.HasValue) token.ExpirationDate = expirationDate;
               if(payload != null) token.Payload = payload;
               if(propertiesJson != null) token.Properties = propertiesJson;
               if(redemptionDate.HasValue) token.RedemptionDate = redemptionDate;
               if(status != null) token.Status = status;
               ctx.Db.OpenIddictSpacetimeToken.Id.Update(token);
               Log.Info($"Reducer: Updated OIDC Token internal ID {internalId}");
          } else {
               Log.Warn($"Reducer: Could not update OIDC Token, internal ID {internalId} not found.");
          }
     }

     [SpacetimeDB.Reducer]
     public static void PruneOidcTokens(ReducerContext ctx, ulong thresholdDate)
     {
          Log.Info($"Reducer: Pruning OIDC tokens created before {thresholdDate}");
           // Find tokens that are inactive/revoked/redeemed/expired and older than the threshold
          var tokensToPrune = ctx.Db.OpenIddictSpacetimeToken.Iter()
              .Where(token => (token.Status == "inactive" || token.Status == "revoked" || token.Status == "redeemed" || (token.ExpirationDate.HasValue && token.ExpirationDate < thresholdDate)) &&
                              token.CreationDate.HasValue && token.CreationDate < thresholdDate)
              .ToList();

          int count = 0;
          foreach (var token in tokensToPrune)
          {
               ctx.Db.OpenIddictSpacetimeToken.Id.Delete(token.Id);
               count++;
          }
          Log.Info($"Reducer: Pruned {count} OIDC tokens.");
     }


    // --- Scope Reducers ---
    [SpacetimeDB.Reducer]
    public static void CreateOidcScope(ReducerContext ctx, string oidcScopeId, string name, string? description, string? descriptionsJson, string? displayName, string? displayNamesJson, string? propertiesJson, string? resourcesJson)
    {
         if (ctx.Db.OpenIddictSpacetimeScope.Iter().Any(s => s.OpenIddictScopeId == oidcScopeId || s.Name == name)) {
            Log.Error($"Reducer: OIDC Scope with OIDC ID {oidcScopeId} or Name {name} already exists.");
            return; // Or throw
        }
        uint internalId = GetNextId(ctx, "oidcScopeId");
        var newScope = new OpenIddictSpacetimeScope {
            Id = internalId, OpenIddictScopeId = oidcScopeId, Name = name, Description = description, Descriptions = descriptionsJson,
            DisplayName = displayName, DisplayNames = displayNamesJson, Properties = propertiesJson, Resources = resourcesJson
        };
        ctx.Db.OpenIddictSpacetimeScope.Insert(newScope);
        Log.Info($"Reducer: Created OIDC Scope {name} (Internal ID: {internalId})");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteOidcScope(ReducerContext ctx, uint internalId)
    {
        var scope = ctx.Db.OpenIddictSpacetimeScope.Id.Find(internalId);
        if (scope != null) {
            ctx.Db.OpenIddictSpacetimeScope.Id.Delete(internalId);
            Log.Info($"Reducer: Deleted OIDC Scope internal ID {internalId} (Name: {scope.Name})");
        } else {
            Log.Warn($"Reducer: Could not delete OIDC Scope, internal ID {internalId} not found.");
        }
    }

    [SpacetimeDB.Reducer]
    public static void UpdateOidcScope(ReducerContext ctx, uint internalId, string? description, string? descriptionsJson, string? displayName, string? displayNamesJson, string? propertiesJson, string? resourcesJson)
    {
         var scope = ctx.Db.OpenIddictSpacetimeScope.Id.Find(internalId);
         if (scope != null) {
             if(description != null) scope.Description = description;
             if(descriptionsJson != null) scope.Descriptions = descriptionsJson;
             if(displayName != null) scope.DisplayName = displayName;
             if(displayNamesJson != null) scope.DisplayNames = displayNamesJson;
             if(propertiesJson != null) scope.Properties = propertiesJson;
             if(resourcesJson != null) scope.Resources = resourcesJson;
             ctx.Db.OpenIddictSpacetimeScope.Id.Update(scope);
             Log.Info($"Reducer: Updated OIDC Scope internal ID {internalId}");
         } else {
             Log.Warn($"Reducer: Could not update OIDC Scope, internal ID {internalId} not found.");
         }
    }
}