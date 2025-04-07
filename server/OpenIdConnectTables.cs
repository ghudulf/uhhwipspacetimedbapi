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
}