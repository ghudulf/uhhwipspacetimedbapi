 using System.Text;
using SpacetimeDB;

public static partial class Module
{  // User Management
    [SpacetimeDB.Table(Public = true)]
    public partial class UserProfile
    {
        [PrimaryKey]
        public Identity UserId;           // SpacetimeDB Identity

        [Unique]
        public uint LegacyUserId;          // Maps to old SQL UserId (for migration)

        public double? XUID; // XBOX LIVE INSPIRED USER ID NAMED XUID - ADDED FOR LATER USE , WILL BE NICE TO HAVE FOR OPENID CONNECT AND LATER UNIFYING BOTH GUID AND LEGACY USER ID

        [Unique]
        public string Login;              // Primary auth field (unique)

        public string? PasswordHash;      // Keep for migration, phase out later if possible
        public string? Email;
        public string? PhoneNumber;
        public bool IsActive;
        public ulong CreatedAt;           // Unix timestamp (milliseconds)
        public ulong? LastLoginAt;
        public string? LegacyGuid;        // Store as string instead of Guid

        public bool? EmailConfirmed; // email optional so this is just there to make it easier to check if email is confirmed
    }



    [SpacetimeDB.Table(Public = true)]
    public partial class UserSettings
    {
        [PrimaryKey]
        public uint UserSettingId;
        public Identity UserId;
        public bool TotpEnabled;
        public bool WebAuthnEnabled;
        public bool IsEmailNotificationsEnabled;
        public bool IsSmsNotificationsEnabled;
        public bool IsPushNotificationsEnabled;
        public bool IsWhatsAppNotificationsEnabled;
        public bool IsTelegramNotificationsEnabled;
        public bool IsDiscordNotificationsEnabled;

    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Role
    {
        [PrimaryKey]
        public uint RoleId;              // Auto-incremented (use a counter)

        public int LegacyRoleId;          // For migration: old int Role (0, 1, etc.)
        public string Name;
        public string Description;
        public bool IsSystem;             // Prevent deletion of system roles
        public uint Priority;
        public bool IsActive;
        public ulong CreatedAt;
        public ulong UpdatedAt;
        public string? CreatedBy;         // Track who created the role
        public string? UpdatedBy;
        public string? NormalizedName;    // Optional, for faster lookups (uppercase)
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Permission
    {
        [PrimaryKey]
        public uint PermissionId;         // Auto-incremented

        public string Name;
        public string Description;
        public string Category;
        public bool IsActive;
        public ulong CreatedAt;
    }

    // Junction Tables (many-to-many relationships)
    [SpacetimeDB.Table(Public = true)]
    public partial class UserRole
    {
        [PrimaryKey, AutoInc]
        public uint Id;                  // Single primary key

        public Identity UserId;          // References UserProfile.UserId
        public uint RoleId;              // References Role.RoleId

        public ulong AssignedAt;
        public string? AssignedBy;        // Track who assigned the role
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class RolePermission
    {
        [PrimaryKey, AutoInc]
        public uint Id;                  // Single primary key

        public uint RoleId;              // References Role.RoleId
        public uint PermissionId;        // References Permission.PermissionId

        public ulong GrantedAt;
        public string? GrantedBy;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class QRSession
    {
        [PrimaryKey]
        public string SessionId;         // Unique session ID

        public Identity UserId;          // References UserProfile.UserId
        public string ValidationCode;    // Code to validate the QR session
        public ulong ExpiryTime;         // Unix timestamp for expiration
        public string InitiatingDevice;  // "desktop" or "mobile"
        public bool IsUsed;              // Flag to prevent reuse
    } }