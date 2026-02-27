using System.Text;
using SpacetimeDB;

public static partial class Module
{
	// ---------- Table Definitions ----------
	// User Management
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
	}

	// ***** Fleet Management *****

	[SpacetimeDB.Table(Public = true)]
    public partial class Bus
	{
		[PrimaryKey]
		public uint BusId;              // Auto-incremented
		public string Model;
		public string? RegistrationNumber;
		public bool IsActive;           // Add IsActive field
	}

	[SpacetimeDB.Table(Public = true)]
    public partial class Maintenance
	{
		[PrimaryKey]
		public uint MaintenanceId;       // Auto-incremented
		public uint BusId;               // References Bus.BusId
		public ulong LastServiceDate;
		public string? MileageThreshold;
		public string? MaintenanceType;
		public string? ServiceEngineer;
		public string? FoundIssues;
		public ulong NextServiceDate;
		public string? Roadworthiness;
	}

	[SpacetimeDB.Table(Public = true)]
    public partial class Route
	{
		[PrimaryKey]
		public uint RouteId;             // Auto-incremented
		public string StartPoint;
		public string EndPoint;
		public uint DriverId;            // References Employee.EmployeeId
		public uint BusId;               // References Bus.BusId
		public string? TravelTime;          // String or numeric (minutes)

		// Denormalized field for performance (optional, but often helpful)
		// public string BusModel => Bus.Query(b => b.BusId == BusId).FirstOrDefault()?.Model; // Requires a Bus table query.
		public bool IsActive;
		

	}

	[SpacetimeDB.Table(Public = true)]
    public partial class RouteSchedule
	{
		[PrimaryKey]
		public uint ScheduleId;          // Auto-incremented
		public uint RouteId;             // References Route.RouteId
		public string? StartPoint;
		public string[]? RouteStops;
		public string? EndPoint;
		public ulong DepartureTime;
		public ulong ArrivalTime;
		public double Price;
		public uint AvailableSeats;
		public string[]? DaysOfWeek;
		public string[]? BusTypes;      // "MAZ-103", "MAZ-206", etc.
		public bool IsActive;
		public ulong ValidFrom;
		public ulong? ValidUntil;
		public uint? StopDurationMinutes;
		public bool IsRecurring;
		public string[]? EstimatedStopTimes;
		public double[]? StopDistances;
		public string? Notes;
		public ulong CreatedAt;
		public ulong? UpdatedAt;
		public string? UpdatedBy;


	}

	// ***** Employee Management *****
	[SpacetimeDB.Table(Public = true)]
    public partial class Employee
	{
		[PrimaryKey]
		public uint EmployeeId;          // Auto-incremented
		public string Surname;
		public string Name;
		public string? Patronym;         // Optional
		public ulong EmployedSince;    // Unix timestamp
		public uint JobId;               // References Job.JobId
	}

	[SpacetimeDB.Table(Public = true)]
    public partial class Job
	{
		[PrimaryKey]
		public uint JobId;               // Auto-incremented
		public string JobTitle;
		public string? Internship;       // String, since it can have varied text
	}

	// ***** Ticket Management *****

	[SpacetimeDB.Table(Public = true)]
    public partial class Ticket
	{
		[PrimaryKey]
		public uint TicketId;           // Auto-incremented unique identifier for each ticket
		public uint RouteId;            // Foreign key referencing Route.RouteId
		public double TicketPrice;      // Price of the ticket, stored as a double for currency precision
		
		public uint SeatNumber;         // Assigned seat number on the bus
		public string PaymentMethod;    // Payment method used, e.g., "cash", "card"
		public bool IsActive;           // Indicates if the ticket is active and valid for use
		public ulong CreatedAt;         // Timestamp of when the ticket was created
		public ulong? UpdatedAt;        // Timestamp of the last update, if any
		public string? UpdatedBy;       // Identifier of the user who last updated the ticket
        public ulong PurchaseTime;      // Timestamp of when the ticket was purchased
	}

	[SpacetimeDB.Table(Public = true)]
    public partial class Sale
	{
		[PrimaryKey]
		public uint SaleId;             // Auto-incremented unique identifier for each sale
		public ulong SaleDate;          // Timestamp of when the sale occurred
		public uint TicketId;           // Foreign key referencing Ticket.TicketId
		public string TicketSoldToUser; // Name of the user to whom the ticket was sold
		public string TicketSoldToUserPhone; // Phone number of the user to whom the ticket was sold
		public Identity? SellerId;      // Identifier of the seller who processed the sale (can be null initially)
		public string? SaleLocation;    // Optional field to track the location where the sale was made
		public string? SaleNotes;       // Optional field for any additional notes related to the sale
	}

	// ***** Other Tables *****

	// Example: Admin Action Log
	[SpacetimeDB.Table(Name = "admin_action_log", Public = true)]
    public partial class AdminActionLog
	{
		[PrimaryKey]
		public uint LogId;              // Auto-incremented
		public Identity UserId;         // References UserProfile.UserId
		public string Action;
		public string Details;
		public ulong Timestamp;
		public string? IpAddress;     // Optional
		public string? UserAgent;     // Optional
	}
    [SpacetimeDB.Table]
    public partial class Person
    {
        [PrimaryKey, AutoInc]           // Changed from SpacetimeDB.PrimaryKey to PrimaryKey
        public int Id;
        public string Name;
        public int Age;


    }

    [SpacetimeDB.Table]
    public partial class Client
    {
        [PrimaryKey]
        public int Id;

        public string ClientId;
        public string ClientSecret;
    }

    [SpacetimeDB.Table]
    public partial class Passenger
    {
        [PrimaryKey, AutoInc]
        public uint PassengerId;
        public string Name;
        public string Email;
        public string PhoneNumber;
        public bool IsActive;
        public ulong CreatedAt;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
    }

    [SpacetimeDB.Table]
    public partial class PassengerIdCounter
    {
        [PrimaryKey]
        public string Key = "passengerId";
        public uint NextId = 0;
    }

    


    // Counter tables for auto-incrementing IDs
	[SpacetimeDB.Table] 
    public partial class UserIdCounter
    {
		[PrimaryKey]
        public string Key = "userId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class RoleIdCounter
    {
        [PrimaryKey]
        public string Key = "roleId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class PermissionIdCounter
    {
        [PrimaryKey]
        public string Key = "permissionId";
        public uint NextId = 0;
	}
	
	[SpacetimeDB.Table] 
    public partial class BusIdCounter
    {
		[PrimaryKey]
        public string Key = "busId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class MaintenanceIdCounter
    {
        [PrimaryKey]
        public string Key = "maintenanceId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class RouteIdCounter
    {
        [PrimaryKey]
        public string Key = "routeId";
        public uint NextId = 0;
	}
	
	[SpacetimeDB.Table] 
    public partial class ScheduleIdCounter
    {
		[PrimaryKey]
        public string Key = "scheduleId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class EmployeeIdCounter
    {
        [PrimaryKey]
        public string Key = "employeeId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class JobIdCounter
    {
        [PrimaryKey]
        public string Key = "jobId";
        public uint NextId = 0;
	}
	
	[SpacetimeDB.Table] 
    public partial class TicketIdCounter
    {
		[PrimaryKey]
        public string Key = "ticketId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class SaleIdCounter
    {
        [PrimaryKey]
        public string Key = "saleId";
        public uint NextId = 0;
	}
	
	[SpacetimeDB.Table]
    public partial class LogIdCounter
	{
		[PrimaryKey]
        public string Key = "logId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class UserRoleIdCounter
    {
        [PrimaryKey]
        public string Key = "userRoleId";
        public uint NextId = 0;
	}
	
	[SpacetimeDB.Table] 
    public partial class RolePermissionIdCounter
    {
		[PrimaryKey]
        public string Key = "rolePermissionId";
        public uint NextId = 0;
    }

    private static readonly int SaltSize = 16; // 128 bits minimum recommended salt size
    private static readonly int Iterations = 200;  // ADJUST THIS IF YOU WANT MORE SECURITY, THIS IS SPACETIME DB , 5000 IS LITERALLY MAKING THE SERVER DIE AND CRASH AND BURN 
    private static readonly int HashSize = 32; // 256 bits (32 bytes) for the derived key
    

    private static string HashPassword(string password, bool useStaticSalt = false, byte[] staticSalt = null)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;
        
        try
        {
            // SHA-256 implementation for WebAssembly environment
            Log.Info($"Hashing password with SHA-256");

            byte[] salt;
            if (useStaticSalt && staticSalt != null)
            {
                salt = staticSalt;
                Log.Debug("Using provided static salt for password hashing");
            }
            else
            {
                salt = new byte[SaltSize];
                Random r = new Random((int)DateTime.Now.Ticks); // NOT cryptographically secure!But its the only fucking way webassembly wont bitch and fail compile
                r.NextBytes(salt);
                Log.Debug("Using random salt for password hashing");
            }
            
            // Return the salt and hash, concatenated with a colon separator.  This is the format
            // that should be stored in the database.
            byte[] hash = PBKDF2(password, salt, Iterations, HashSize);
            return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            // Log the error
            Log.Error($"SHA-256 WITH PBKDF2 hashing failed: {ex.Message}. Falling back to MurmurHash3.");

            // Fallback to MurmurHash3 if SHA-256 fails
            Log.Info($"Hashing password with MurmurHash3");
            return ComputeMurmurHash3(password);
        }
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            string[] parts = storedHash.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] expectedHash = Convert.FromBase64String(parts[1]);
            byte[] derivedHash = PBKDF2(password, salt, Iterations, HashSize);

            return FixedTimeEquals(expectedHash, derivedHash);
        }
        catch
        {
            return false; // Handle any conversion errors
        }
    }

    // Constant-time byte array comparison
    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
    private static byte[] PBKDF2(string password, byte[] salt, int iterations, int outputBytes)
    {
        Log.Info("PBKDF2: Starting password hashing");
        Log.Debug($"PBKDF2: Parameters - iterations: {iterations}, outputBytes: {outputBytes}");
        
        Log.Debug("PBKDF2: Converting password to UTF-8 bytes");
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        
        Log.Debug("PBKDF2: Generating initial hash with HMACSHA256");
        byte[] hash = HMACSHA256(passwordBytes, salt); // Initial hash

        Log.Debug($"PBKDF2: Beginning iteration process (1 to {iterations-1})");
        for (int i = 1; i < iterations; i++)
        {
            if (i % 1000 == 0)
            {
                Log.Debug($"PBKDF2: Completed {i} iterations");
            }
            hash = HMACSHA256(passwordBytes, hash); // Repeatedly hash
        }

        Log.Debug($"PBKDF2: Iterations complete, resizing hash to {outputBytes} bytes");
        // Truncate or pad to the desired output size (optional, but good practice)
        Array.Resize(ref hash, outputBytes);
        
        Log.Info("PBKDF2: Password hashing complete");
        return hash;
    }
    private static byte[] HMACSHA256(byte[] key, byte[] message)
    {
        Log.Debug("HMACSHA256: Starting HMAC operation");
        
        // Simplified HMAC (NOT cryptographically secure as a standalone HMAC).
        // This is acceptable ONLY within the context of PBKDF2.
        Log.Debug($"HMACSHA256: Combining key ({key.Length} bytes) and message ({message.Length} bytes)");
        byte[] combined = new byte[key.Length + message.Length];
        Array.Copy(key, 0, combined, 0, key.Length);
        Array.Copy(message, 0, combined, key.Length, message.Length);

        // Convert the combined array to a hexadecimal string
        Log.Debug("HMACSHA256: Converting combined array to hexadecimal string");
        string combinedString = Convert.ToHexString(combined).ToLower();

        // Hash the combined string using your existing ComputeSha256 function
        Log.Debug("HMACSHA256: Computing SHA-256 hash of combined string");
        string hashString = ComputeSha256(combinedString);

        // Convert the hexadecimal hash string back to a byte array
        Log.Debug("HMACSHA256: Converting hash string back to byte array");
        byte[] result = Convert.FromHexString(hashString);
        
        Log.Debug($"HMACSHA256: HMAC operation complete, returning {result.Length} bytes");
        return result;
    }

    // SHA-256 implementation compatible with WebAssembly
    public static string ComputeSha256(string input)
    {
        Log.Debug("ComputeSha256: Starting SHA-256 computation");
        Log.Debug($"ComputeSha256: Input string length: {input.Length} characters");
        
        // Convert the input string to bytes using UTF-8 encoding.
        Log.Debug("ComputeSha256: Converting input to UTF-8 bytes");
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        Log.Debug($"ComputeSha256: Input converted to {inputBytes.Length} bytes");

        // Initial hash values (h0 through h7) - These are the correct constants.
        Log.Debug("ComputeSha256: Initializing hash values");
        uint[] h = {
            0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
            0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
        };

        // Round constants (k) - These are the correct constants.
        Log.Debug("ComputeSha256: Setting up round constants");
        uint[] k = {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        // 1. Pre-processing (Padding)
        Log.Debug("ComputeSha256: Beginning pre-processing (padding)");

        long bitLength = (long)inputBytes.Length * 8; // Total length in *bits*.  Use long to avoid overflow.
        Log.Debug($"ComputeSha256: Total bit length: {bitLength}");

        // a. Append '1' bit followed by zeros.  '1' bit is 0x80 as a byte.
        Log.Debug("ComputeSha256: Appending '1' bit");
        List<byte> paddedBytes = new List<byte>(inputBytes);
        paddedBytes.Add(0x80);

        // b. Append zeros until the length is congruent to 448 mod 512 (bits).
        //    This means the message length, in bits, must be 64 bits less than a multiple of 512.
        Log.Debug("ComputeSha256: Padding with zeros");
        while ((paddedBytes.Count * 8) % 512 != 448)
        {
            paddedBytes.Add(0);
        }
        Log.Debug($"ComputeSha256: After padding, byte count: {paddedBytes.Count}");

        // c. Append the original length (in bits) as a 64-bit big-endian integer.
        //    Crucially, we *must* append these bytes in big-endian order.
        Log.Debug("ComputeSha256: Appending original length as 64-bit big-endian integer");
        byte[] lengthBytes = BitConverter.GetBytes(bitLength);
        if (BitConverter.IsLittleEndian)
        {
            Log.Debug("ComputeSha256: System is little-endian, reversing bytes for big-endian representation");
            Array.Reverse(lengthBytes); // Ensure big-endian for SHA-256
        }
        paddedBytes.AddRange(lengthBytes);
        Log.Debug($"ComputeSha256: Final padded message length: {paddedBytes.Count} bytes");

        // Now paddedBytes contains the complete padded message.

        // 2. Processing in 64-byte (512-bit) chunks.
        Log.Debug("ComputeSha256: Beginning message processing in 64-byte chunks");
        byte[] message = paddedBytes.ToArray();
        int chunkCount = message.Length / 64;
        Log.Debug($"ComputeSha256: Processing {chunkCount} chunks");
        
        for (int i = 0; i < message.Length; i += 64)
        {
            Log.Debug($"ComputeSha256: Processing chunk {i/64 + 1} of {chunkCount}");
            
            // a. Create message schedule w[0..63]
            uint[] w = new uint[64];

            // b. Copy chunk into first 16 words (w[0..15]) of w.
            Log.Debug("ComputeSha256: Copying chunk into first 16 words of message schedule");
            for (int j = 0; j < 16; j++)
            {
                // Explicitly handle big-endian conversion.
                w[j] = ((uint)message[i + (j * 4)] << 24) |
                       ((uint)message[i + (j * 4) + 1] << 16) |
                       ((uint)message[i + (j * 4) + 2] << 8) |
                       ((uint)message[i + (j * 4) + 3]);
            }

            // c. Extend w[16..63]
            Log.Debug("ComputeSha256: Extending message schedule to 64 words");
            for (int j = 16; j < 64; j++)
            {
                uint s0 = RotateRight(w[j - 15], 7) ^ RotateRight(w[j - 15], 18) ^ (w[j - 15] >> 3);
                uint s1 = RotateRight(w[j - 2], 17) ^ RotateRight(w[j - 2], 19) ^ (w[j - 2] >> 10);
                w[j] = w[j - 16] + s0 + w[j - 7] + s1;
            }

            // d. Initialize working variables (a through h) to current hash value.
            Log.Debug("ComputeSha256: Initializing working variables");
            uint a = h[0];
            uint b = h[1];
            uint c = h[2];
            uint d = h[3];
            uint e = h[4];
            uint f = h[5];
            uint g = h[6];
            uint hh = h[7]; // 'h' is a keyword, so use 'hh'

            // e. Compression function main loop (64 rounds)
            Log.Debug("ComputeSha256: Starting compression function (64 rounds)");
            for (int j = 0; j < 64; j++)
            {
                uint S1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint ch = (e & f) ^ ((~e) & g);
                uint temp1 = hh + S1 + ch + k[j] + w[j];
                uint S0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint temp2 = S0 + maj;

                hh = g;
                g = f;
                f = e;
                e = d + temp1; // Careful:  *Unsigned* integer addition (wraps around).
                d = c;
                c = b;
                b = a;
                a = temp1 + temp2;
            }
            Log.Debug("ComputeSha256: Compression function complete");

            // f. Add the compressed chunk to the current hash value.
            Log.Debug("ComputeSha256: Adding compressed chunk to current hash value");
            h[0] += a;
            h[1] += b;
            h[2] += c;
            h[3] += d;
            h[4] += e;
            h[5] += f;
            h[6] += g;
            h[7] += hh;
        }


        // 3. Produce the final hash value (big-endian).
        Log.Debug("ComputeSha256: Producing final hash value");
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            result.Append(h[i].ToString("x8")); // "x8" ensures 8 hex characters, lowercase.
        }

        Log.Debug("ComputeSha256: SHA-256 computation complete");
        return result.ToString();
    }

    // Helper function for right rotation.
    private static uint RotateRight(uint value, int shift)
    {
        return (value >> shift) | (value << (32 - shift));
    }

    // MurmurHash3 implementation as fallback
    private static string ComputeMurmurHash3(string key)
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(key);
        int length = data.Length;
        int nblocks = length / 4;
        
        uint seed = 42; // Fixed seed for consistency
        uint h1 = seed;
        
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;
        
        // Body
        for (int i = 0; i < nblocks; i++)
        {
            int index = i * 4;
            uint k1 = BitConverter.ToUInt32(data, index);
            
            k1 *= c1;
            k1 = RotateLeft(k1, 15);
            k1 *= c2;
            
            h1 ^= k1;
            h1 = RotateLeft(h1, 13);
            h1 = h1 * 5 + 0xe6546b64;
        }
        
        // Tail
        uint tail = 0;
        int remainder = length & 3;
        if (remainder > 0)
        {
            int index = nblocks * 4;
            if (remainder >= 3) tail |= (uint)data[index + 2] << 16;
            if (remainder >= 2) tail |= (uint)data[index + 1] << 8;
            if (remainder >= 1) tail |= data[index];
            
            tail *= c1;
            tail = RotateLeft(tail, 15);
            tail *= c2;
            h1 ^= tail;
        }
        
        // Finalization
        h1 ^= (uint)length;
        h1 = FMix(h1);
        
        return h1.ToString();
    }

    private static uint RotateLeft(uint x, int r)
    {
        return (x << r) | (x >> (32 - r));
    }

    private static uint FMix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }

    // Helper method to get the next ID from a counter table
    private static uint GetNextId(ReducerContext ctx, string counterKey)
    {
        // Use if-else if blocks for comparison of string keys.
        if (counterKey == "userId")
        {
            var counter = ctx.Db.UserIdCounter.Key.Find("userId");
            if (counter == null)
            {
                counter = ctx.Db.UserIdCounter.Insert(new UserIdCounter { Key = "userId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.UserIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "roleId")
        {
            var counter = ctx.Db.RoleIdCounter.Key.Find("roleId");
            if (counter == null)
            {
                counter = ctx.Db.RoleIdCounter.Insert(new RoleIdCounter { Key = "roleId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.RoleIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "permissionId")
        {
            var counter = ctx.Db.PermissionIdCounter.Key.Find("permissionId");
            if (counter == null)
            {
                counter = ctx.Db.PermissionIdCounter.Insert(new PermissionIdCounter { Key = "permissionId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.PermissionIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "busId")
        {
            var counter = ctx.Db.BusIdCounter.Key.Find("busId");
            if (counter == null)
            {
                counter = ctx.Db.BusIdCounter.Insert(new BusIdCounter { Key = "busId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.BusIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "maintenanceId")
        {
            var counter = ctx.Db.MaintenanceIdCounter.Key.Find("maintenanceId");
            if (counter == null)
            {
                counter = ctx.Db.MaintenanceIdCounter.Insert(new MaintenanceIdCounter { Key = "maintenanceId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.MaintenanceIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "routeId")
        {
            var counter = ctx.Db.RouteIdCounter.Key.Find("routeId");
            if (counter == null)
            {
                counter = ctx.Db.RouteIdCounter.Insert(new RouteIdCounter { Key = "routeId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.RouteIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "scheduleId")
        {
            var counter = ctx.Db.ScheduleIdCounter.Key.Find("scheduleId");
            if (counter == null)
            {
                counter = ctx.Db.ScheduleIdCounter.Insert(new ScheduleIdCounter { Key = "scheduleId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.ScheduleIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "employeeId")
        {
            var counter = ctx.Db.EmployeeIdCounter.Key.Find("employeeId");
            if (counter == null)
            {
                counter = ctx.Db.EmployeeIdCounter.Insert(new EmployeeIdCounter { Key = "employeeId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.EmployeeIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "jobId")
        {
            var counter = ctx.Db.JobIdCounter.Key.Find("jobId");
            if (counter == null)
            {
                counter = ctx.Db.JobIdCounter.Insert(new JobIdCounter { Key = "jobId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.JobIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "ticketId")
        {
            var counter = ctx.Db.TicketIdCounter.Key.Find("ticketId");
            if (counter == null)
            {
                counter = ctx.Db.TicketIdCounter.Insert(new TicketIdCounter { Key = "ticketId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.TicketIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "saleId")
        {
            var counter = ctx.Db.SaleIdCounter.Key.Find("saleId");
            if (counter == null)
            {
                counter = ctx.Db.SaleIdCounter.Insert(new SaleIdCounter { Key = "saleId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.SaleIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "logId")
        {
            var counter = ctx.Db.LogIdCounter.Key.Find("logId");
            if (counter == null)
            {
                counter = ctx.Db.LogIdCounter.Insert(new LogIdCounter { Key = "logId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.LogIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "userRoleId")
        {
            var counter = ctx.Db.UserRoleIdCounter.Key.Find("userRoleId");
            if (counter == null)
            {
                counter = ctx.Db.UserRoleIdCounter.Insert(new UserRoleIdCounter { Key = "userRoleId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.UserRoleIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else if (counterKey == "rolePermissionId")
        {
            var counter = ctx.Db.RolePermissionIdCounter.Key.Find("rolePermissionId");
            if (counter == null)
            {
                counter = ctx.Db.RolePermissionIdCounter.Insert(new RolePermissionIdCounter { Key = "rolePermissionId", NextId = 1 });
            }
            else
            {
                counter.NextId++;
                ctx.Db.RolePermissionIdCounter.Key.Update(counter);
            }
            return counter.NextId;
        }
        else
        {
            Log.Error($"Unknown counter key: {counterKey}");
            return 0; // Return a default value
        }
    }

    [SpacetimeDB.Reducer(SpacetimeDB.ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        // Log the start of initialization
        Log.Info("Initializing the system...");
        // Change the order of initialization to avoid permission issues
        // 1. Initialize Admin User first (so we have an identity with permissions)
        InitializeAdminUser(ctx);
        // 2. Initialize Permissions
        InitializePermissions(ctx);
        // 3. Initialize Roles
        InitializeRoles(ctx);
        // 4. Initialize Jobs
        InitializeJobs(ctx);
        // 5. Initialize Employees
        InitializeEmployees(ctx);
        // 6. Initialize Buses
        InitializeBuses(ctx);
        // 7. Initialize Routes
        InitializeRoutes(ctx);
        // 8. Initialize Tickets
        InitializeTickets(ctx);
        // 9. Initialize Maintenance Records
        InitializeMaintenance(ctx);
        // 10. Initialize Route Schedules
        InitializeRouteSchedules(ctx);
        // 11. Initialize Sales
        InitializeSales(ctx);
        // Log successful initialization
        Log.Info("System initialized successfully");
    }

    private static void InitializeAdminUser(ReducerContext ctx)
    {
        Log.Info("Initializing admin user...");
        // Check if admin user already exists
        if (!ctx.Db.UserProfile.Iter().Any(u => u.Login == "admin"))
        {
            // Get the next user ID
            uint userId = GetNextId(ctx, "userId");
            
            // Create admin user with the module's identity
            var admin = new UserProfile
            {
                UserId = ctx.Sender,         // Module's identity
                LegacyUserId = userId,
                Login = "admin",
                Email = "admin@example.com",
                PhoneNumber = "+375333000000",
                PasswordHash = HashPassword("admin"),
                IsActive = true,
                CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                LastLoginAt = null,
                LegacyGuid = Guid.NewGuid().ToString()
            };
            
            ctx.Db.UserProfile.Insert(admin);
            Log.Info("Admin user created successfully");
            
            // Create admin role directly here instead of waiting for InitializeRoles
            if (!ctx.Db.Role.Iter().Any(r => r.Name == "Administrator"))
            {
                uint roleId = GetNextId(ctx, "roleId");
                var adminRole = new Role
                {
                    RoleId = roleId,
                    LegacyRoleId = 1,
                    Name = "Administrator",
                    Description = "Full system access",
                    IsSystem = true,
                    Priority = 100,
                    IsActive = true,
                    CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    CreatedBy = "System",
                    UpdatedBy = "System",
                    NormalizedName = "ADMINISTRATOR"
                };
                ctx.Db.Role.Insert(adminRole);
                
                // Assign admin role to admin user using userRoleId counter
                uint userRoleId = GetNextId(ctx, "userRoleId");
                var userRole = new UserRole
                {
                    Id = userRoleId,
                    UserId = admin.UserId,
                    RoleId = adminRole.RoleId,
                    AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    AssignedBy = "System"
                };
                ctx.Db.UserRole.Insert(userRole);
                Log.Info("Admin role created and assigned to admin user");
            }
            
            // Create a placeholder for guest user - will be claimed later
            // We'll use a special placeholder identity derived from the module's identity
            var placeholderIdentity = new Identity();
            userId = GetNextId(ctx, "userId");
            var guest = new UserProfile
            {
                UserId = placeholderIdentity,
                LegacyUserId = userId,
                Login = "guest",
                Email = "guest@example.com",
                PhoneNumber = "+375333000001",
                PasswordHash = HashPassword("gX9#mP2$kL5"),
                IsActive = false, // Not active until claimed
                CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                LastLoginAt = null,
                LegacyGuid = Guid.NewGuid().ToString()
            };
            
            ctx.Db.UserProfile.Insert(guest);
            Log.Info("Guest user placeholder created successfully");
            
            // Create user role directly here
            if (!ctx.Db.Role.Iter().Any(r => r.Name == "User"))
            {
                uint roleId = GetNextId(ctx, "roleId");
                var userRoleObj = new Role
                {
                    RoleId = roleId,
                    LegacyRoleId = 0,
                    Name = "User",
                    Description = "Basic access",
                    IsSystem = true,
                    Priority = 1,
                    IsActive = true,
                    CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    CreatedBy = "System",
                    UpdatedBy = "System",
                    NormalizedName = "USER"
                };
                ctx.Db.Role.Insert(userRoleObj);
                
                // Assign user role to guest user with proper ID
                uint guestUserRoleId = GetNextId(ctx, "userRoleId");
                var guestUserRole = new UserRole
                {
                    Id = guestUserRoleId,
                    UserId = guest.UserId,
                    RoleId = userRoleObj.RoleId,
                    AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    AssignedBy = "System"
                };
                ctx.Db.UserRole.Insert(guestUserRole);
                Log.Info("User role created and assigned to guest user placeholder");
            }
        }
    }

    private static void InitializePermissions(ReducerContext ctx)
    {
        // Define and insert permissions here
        var permissions = new[]
        {
            // User Management
            ("users.view", "View users", "User Management"),
            ("users.create", "Create users", "User Management"),
            ("users.edit", "Edit users", "User Management"),
            ("users.delete", "Delete users", "User Management"),
            
            // Role Management
            ("roles.view", "View roles", "Role Management"),
            ("roles.create", "Create roles", "Role Management"),
            ("roles.edit", "Edit roles", "Role Management"),
            ("roles.delete", "Delete roles", "Role Management"),
            
            // Add "assign_roles" and "grant_permissions" permissions
            ("assign_roles", "Assign roles to users", "Role Management"),
            ("grant_permissions", "Grant permissions to roles", "Permission Management"),
            
            // Employee Management
            ("employees.view", "View employees", "Employee Management"),
            ("employees.create", "Create employees", "Employee Management"),
            ("employees.edit", "Edit employees", "Employee Management"),
            ("employees.delete", "Delete employees", "Employee Management"),
            
            // Bus Management
            ("buses.view", "View buses", "Bus Management"),
            ("buses.create", "Create buses", "Bus Management"),
            ("buses.edit", "Edit buses", "Bus Management"),
            ("buses.delete", "Delete buses", "Bus Management"),
            
            // Route Management
            ("routes.view", "View routes", "Route Management"),
            ("routes.create", "Create routes", "Route Management"),
            ("routes.edit", "Edit routes", "Route Management"),
            ("routes.delete", "Delete routes", "Route Management"),
            
            // Ticket Management
            ("tickets.view", "View tickets", "Ticket Management"),
            ("tickets.create", "Create tickets", "Ticket Management"),
            ("tickets.edit", "Edit tickets", "Ticket Management"),
            ("tickets.delete", "Delete tickets", "Ticket Management"),
            
            // Sales Management
            ("sales.view", "View sales", "Sales Management"),
            ("sales.create", "Create sales", "Sales Management"),
            ("sales.edit", "Edit sales", "Sales Management"),
            ("sales.delete", "Delete sales", "Sales Management"),
            
            // Maintenance Management
            ("maintenance.view", "View maintenance records", "Maintenance Management"),
            ("maintenance.create", "Create maintenance records", "Maintenance Management"),
            ("maintenance.edit", "Edit maintenance records", "Maintenance Management"),
            ("maintenance.delete", "Delete maintenance records", "Maintenance Management"),
            
            // Reports
            ("reports.view", "View reports", "Reports"),
            ("reports.create", "Create reports", "Reports"),
            ("reports.export", "Export reports", "Reports")
        };
        
        foreach (var (name, description, category) in permissions)
        {
            CreatePermission(ctx, name, description, category);
        }
        
        // Directly grant the admin user the necessary permissions
        var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin");
        var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
        
        if (adminUser != null && adminRole != null)
        {
            // Find or create the grant_permissions permission
            var grantPermission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == "grant_permissions");
            if (grantPermission == null)
            {
                uint permId = GetNextId(ctx, "permissionId");
                grantPermission = new Permission
                {
                    PermissionId = permId,
                    Name = "grant_permissions",
                    Description = "Grant permissions to roles",
                    Category = "Permission Management",
                    IsActive = true,
                    CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
                };
                ctx.Db.Permission.Insert(grantPermission);
            }
            
            // Directly create the role permission without using GrantPermissionToRole
            if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == adminRole.RoleId && rp.PermissionId == grantPermission.PermissionId))
            {
                uint rolePermId = GetNextId(ctx, "rolePermissionId");
                var rolePermission = new RolePermission
                {
                    Id = rolePermId,
                    RoleId = adminRole.RoleId,
                    PermissionId = grantPermission.PermissionId,
                    GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                    GrantedBy = "System"
                };
                ctx.Db.RolePermission.Insert(rolePermission);
            }
        }
    }

    private static void InitializeRoles(ReducerContext ctx)
    {
        // Create default roles if they don't already exist
        if (!ctx.Db.Role.Iter().Any(r => r.Name == "Administrator"))
        {
            CreateRole(ctx, 1, "Administrator", "Full system access", true, 100);
        }
        
        if (!ctx.Db.Role.Iter().Any(r => r.Name == "User"))
        {
            CreateRole(ctx, 0, "User", "Basic access", true, 1);
        }
        
        if (!ctx.Db.Role.Iter().Any(r => r.Name == "Manager"))
        {
            CreateRole(ctx, 2, "Manager", "System management access", true, 50);
        }

        // Get the role IDs for roles
        var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
        var userRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
        var managerRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Manager");

        // Check if the roles were successfully created and retrieved
        if (adminRole != null && userRole != null && managerRole != null)
        {
            // Assign all permissions to the Administrator role
            var allPermissions = ctx.Db.Permission.Iter().ToList();
            foreach (var perm in allPermissions)
            {
                // More robust uniqueness check - check both RoleId and PermissionId together
                if (!ctx.Db.RolePermission.Iter().Any(rp => 
                    rp.RoleId == adminRole.RoleId && 
                    rp.PermissionId == perm.PermissionId))
                {
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = adminRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
            }
            
            // Assign view permissions to user role
            var viewPermissions = ctx.Db.Permission.Iter()
                .Where(p => p.Name.EndsWith(".view"))
                .ToList();
            foreach (var perm in viewPermissions)
            {
                // More robust uniqueness check
                if (!ctx.Db.RolePermission.Iter().Any(rp => 
                    rp.RoleId == userRole.RoleId && 
                    rp.PermissionId == perm.PermissionId))
                {
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = userRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
            }
            
            // Assign manager permissions (view + create + edit)
            var managerPermissions = ctx.Db.Permission.Iter()
                .Where(p => p.Name.EndsWith(".view") || p.Name.EndsWith(".create") || p.Name.EndsWith(".edit"))
                .ToList();
            foreach (var perm in managerPermissions)
            {
                // More robust uniqueness check
                if (!ctx.Db.RolePermission.Iter().Any(rp => 
                    rp.RoleId == managerRole.RoleId && 
                    rp.PermissionId == perm.PermissionId))
                {
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = managerRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
            }
        }
        else
        {
            Log.Warn("Warning: could not seed admin and user with roles and permissions.");
        }
    }

    private static void CreatePermission(ReducerContext ctx, string name, string description, string category)
    {
        if (ctx.Db.Permission.Iter().Any(p => p.Name == name))
        {
            return; // Permission already exists
        }
        
        uint permissionId = GetNextId(ctx, "permissionId");
        
        var permission = new Permission
        {
            PermissionId = permissionId,
            Name = name,
            Description = description,
            Category = category,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 // Convert to milliseconds
        };
        ctx.Db.Permission.Insert(permission);
    }

    private static void CreateRole(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority)
    {
        if (ctx.Db.Role.Iter().Any(r => r.Name == name))
        {
            return; // Role already exists
        }
        
        uint roleId = GetNextId(ctx, "roleId");
        
        var role = new Role
        {
            RoleId = roleId,
            LegacyRoleId = legacyRoleId,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            Priority = priority,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            CreatedBy = "System",
            UpdatedBy = "System",
            NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
    }

    [SpacetimeDB.Reducer(SpacetimeDB.ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        // Check if this identity has already claimed a user
        var existingUser = ctx.Db.UserProfile.UserId.Find(ctx.Sender);
        
        if (existingUser != null && existingUser.IsActive)
        {
            // User already exists and is active
            Log.Info($"User {existingUser.Login} connected with identity {ctx.Sender}");
            return;
        }
        
        // This is a new connection - they'll need to authenticate through your API
        // and then call a reducer to claim their account
        Log.Info($"New client connected with identity {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void RegisterUser(ReducerContext ctx, string login, string password, string email, string phoneNumber, uint? roleId = null, string? roleName = null)
    {
        // Check if a user with the same login already exists
        if (ctx.Db.UserProfile.Iter().Any(u => u.Login == login))
        {
            // Throw exception if login is already taken
            throw new Exception("Login already exists.");
        }

        // Get the next user ID from the counter
        uint userId = GetNextId(ctx, "userId");

        // Hash the password
        string hashedPassword = HashPassword(password);

        // Create the new user
        var user = new UserProfile
        {
            UserId = ctx.Sender,
            LegacyUserId = userId,
            Login = login,
            PasswordHash = hashedPassword,
            Email = email,
            PhoneNumber = phoneNumber,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            LastLoginAt = null,
            LegacyGuid = Guid.NewGuid().ToString()
        };
        ctx.Db.UserProfile.Insert(user);
        Log.Info($"User {login} registered successfully");

        // Assign role if specified by roleId
        if (roleId.HasValue)
        {
            // Validate that the role exists
            if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId.Value))
            {
                throw new Exception($"Role with ID {roleId.Value} not found.");
            }

            AssignRole(ctx, user.UserId, roleId.Value);
            Log.Info($"Assigned role {roleId.Value} to user {login}");
        }
        // Assign role if specified by roleName
        else if (!string.IsNullOrEmpty(roleName))
        {
            // Find role by name
            var role = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == roleName);
            if (role == null)
            {
                throw new Exception($"Role with name '{roleName}' not found.");
            }

            AssignRole(ctx, user.UserId, role.RoleId);
            Log.Info($"Assigned role '{roleName}' to user {login}");
        }
        else
        {
            // Assign default "User" role if no role specified
        var userRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
        if (userRole != null)
        {
            AssignRole(ctx, user.UserId, userRole.RoleId);
                Log.Info($"Assigned default 'User' role to user {login}");
            }
            else
            {
                Log.Error("Default 'User' role not found!");
            }
        }
    }

    [SpacetimeDB.Reducer]
    public static void AuthenticateUser(ReducerContext ctx, string login, string password)
    {
        var user = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == login);
        
        // Check all cases when authentication should return as unsuccessful
        if (user == null || !user.IsActive)
        {
            Log.Info($"Authentication failed for user {login}");
            return;
        }

        // Verify password using the VerifyPassword method
        if (!VerifyPassword(password, user.PasswordHash))
        {
            Log.Info($"Authentication failed for user {login}: invalid password");
            return;
        }

        // Get user's roles (for JWT claims)
        var roles = ctx.Db.UserRole.Iter()
            .Where(ur => ur.UserId == user.UserId)
            .Join(ctx.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r)
            .Where(r => r.IsActive)
            .Select(r => r.Name)
            .ToArray();

        // Update LastLoginAt
        user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.UserId.Update(user);

        // Log the successful authentication
        Log.Info($"User {user.Login} authenticated successfully with roles: {string.Join(", ", roles)}");
    }

    [SpacetimeDB.Reducer]
    public static void CreateQRSession(ReducerContext ctx, string sessionId, Identity userId, string validationCode, ulong expiryTime, string initiatingDevice)
    {
        // Check if the user exists
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            // Throw exception if user doesn't exist
            throw new Exception("User not found.");
        }

        // Check if the session already exists
        var existingSession = ctx.Db.QRSession.SessionId.Find(sessionId);
        if (existingSession != null)
        {
            // Throw exception if session ID is already in use
            throw new Exception("Session already exists.");
        }

        // Create a new QR session with the provided information
        var session = new QRSession
        {
            SessionId = sessionId,           // Set the session ID
            UserId = userId,                 // Set the user ID
            ValidationCode = validationCode, // Set the validation code
            ExpiryTime = expiryTime,         // Set the expiry time
            InitiatingDevice = initiatingDevice, // Set the initiating device
            IsUsed = false                   // Mark as unused initially
        };
        // Insert the new session into the database
        ctx.Db.QRSession.Insert(session);
        // Log the session creation
        Log.Info($"QR session created for user {userId} with session ID {sessionId}");
    }

    [SpacetimeDB.Reducer]
    public static void ValidateQRCode(ReducerContext ctx, string sessionId, string validationCode)
    {
        // Find the session with the given ID
        var session = ctx.Db.QRSession.SessionId.Find(sessionId);
        if (session == null)
        {
            // Throw exception if session doesn't exist
            throw new Exception("QR session not found.");
        }

        // Check if the session is expired
        var currentTime = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;  // Current time in milliseconds
        if (currentTime > session.ExpiryTime)
        {
            // Delete expired session
            ctx.Db.QRSession.SessionId.Delete(sessionId);
            // Throw exception for expired session
            throw new Exception("QR session has expired.");
        }

        // Check if the session is already used
        if (session.IsUsed)
        {
            // Throw exception if session was already used
            throw new Exception("QR session has already been used.");
        }

        // Check if the validation code matches
        if (session.ValidationCode != validationCode)
        {
            // Throw exception for invalid code
            throw new Exception("Invalid validation code.");
        }

        // Mark the session as used
        session.IsUsed = true;
        ctx.Db.QRSession.SessionId.Update(session);  // Update the session record

        // Update the user's last login time
        var user = ctx.Db.UserProfile.UserId.Find(session.UserId);
        if (user != null)
        {
            // Set the last login time to current time
            user.LastLoginAt = currentTime;
            ctx.Db.UserProfile.UserId.Update(user);  // Update the user record
        }

        // Log successful validation
        Log.Info($"QR code validated for session {sessionId}");
    }

    [SpacetimeDB.Reducer]
    public static void UseQRSession(ReducerContext ctx, string sessionId)
    {
        // Find the session with the given ID
        var session = ctx.Db.QRSession.SessionId.Find(sessionId);
        if (session == null)
        {
            // Throw exception if session doesn't exist
            throw new Exception("QR session not found.");
        }

        // Check if the session is expired
        var currentTime = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;  // Current time in milliseconds
        if (currentTime > session.ExpiryTime)
        {
            // Delete expired session
            ctx.Db.QRSession.SessionId.Delete(sessionId);
            // Throw exception for expired session
            throw new Exception("QR session has expired.");
        }

        // Check if the session is already used
        if (session.IsUsed)
        {
            // Throw exception if session was already used
            throw new Exception("QR session has already been used.");
        }

        // Mark the session as used
        session.IsUsed = true;
        ctx.Db.QRSession.SessionId.Update(session);  // Update the session record

        // Update the user's last login time
        var user = ctx.Db.UserProfile.UserId.Find(session.UserId);
        if (user != null)
        {
            // Set the last login time to current time
            user.LastLoginAt = currentTime;
            ctx.Db.UserProfile.UserId.Update(user);  // Update the user record
        }

        // Log successful session use
        Log.Info($"QR session {sessionId} used successfully");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteQRSession(ReducerContext ctx, string sessionId)
    {
        // Find the session with the given ID
        var session = ctx.Db.QRSession.SessionId.Find(sessionId);
        if (session == null)
        {
            // Throw exception if session doesn't exist
            throw new Exception("QR session not found.");
        }

        // Delete the session from the database
        ctx.Db.QRSession.SessionId.Delete(sessionId);
        // Log the deletion
        Log.Info($"QR session {sessionId} deleted");
    }

    [SpacetimeDB.Reducer]
    public static void AssignRole(ReducerContext ctx, Identity userId, uint roleId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "assign_roles"))
            throw new Exception("Unauthorized");

        // Validate that the user exists
        if (!ctx.Db.UserProfile.Iter().Any(u => u.UserId == userId))
            throw new Exception("User not found");
        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found");

        // Prevent duplicate role assignments
        if (ctx.Db.UserRole.Iter().Any(ur => ur.UserId == userId && ur.RoleId == roleId))
            throw new Exception("Role already assigned");

        // Create a new user-role assignment
        var userRole = new UserRole
        {
            Id = 0, // Auto-increment will assign this
            UserId = userId,  // Set the user ID
            RoleId = roleId,  // Set the role ID
            AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set assignment time
            AssignedBy = ctx.Sender.ToString() // Track who assigned the role
        };
        // Insert the new assignment into the database
        ctx.Db.UserRole.Insert(userRole);
    }

    [SpacetimeDB.Reducer]
    public static void GrantPermissionToRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "grant_permissions"))
        {
            throw new Exception("Unauthorized: You do not have permission to grant permissions.");
        }

        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found.");
        // Validate that the permission exists
        if (!ctx.Db.Permission.Iter().Any(p => p.PermissionId == permissionId))
            throw new Exception("Permission not found.");

        // Check for existing assignment to prevent duplicates
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId))
            throw new Exception("Permission already granted to this role.");

        // Create a new role-permission assignment
        var rolePermission = new RolePermission
        {
            Id = 0, // Auto-increment will assign this
            RoleId = roleId,  // Set the role ID
            PermissionId = permissionId,  // Set the permission ID
            GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set grant time
            GrantedBy = ctx.Sender.ToString()  // Track who granted the permission
        };
        // Insert the new assignment into the database
        ctx.Db.RolePermission.Insert(rolePermission);
    }

    [SpacetimeDB.Reducer]
    public static void RevokePermissionFromRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "grant_permissions"))
        {
            throw new Exception("Unauthorized: You do not have permission to revoke permissions.");
        }

        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found.");
        // Validate that the permission exists
        if (!ctx.Db.Permission.Iter().Any(p => p.PermissionId == permissionId))
            throw new Exception("Permission not found.");

        // Find the role-permission assignment
        var rolePermission = ctx.Db.RolePermission.Iter()
            .FirstOrDefault(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        // Check if the assignment exists
        if (rolePermission == null)
            throw new Exception("Permission is not granted to this role.");

        // Delete the role-permission assignment
        ctx.Db.RolePermission.Id.Delete(rolePermission.Id);
        // Log the revocation
        Log.Info($"Permission {permissionId} revoked from role {roleId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void RemoveRole(ReducerContext ctx, Identity userId, uint roleId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "assign_roles"))
            throw new Exception("Unauthorized: You do not have permission to remove roles.");

        // Validate that the user exists
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
            throw new Exception("User not found.");
        
        // Validate that the role exists
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
            throw new Exception("Role not found.");

        // Prevent removing the last admin role
        if (role.Name == "Administrator")
        {
            // Find the Administrator role
            var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole != null)
            {
                // Count how many users have the admin role
                var adminCount = ctx.Db.UserRole.Iter()
                    .Where(ur => ur.RoleId == adminRole.RoleId)
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .Count();

                // If this is the last admin and it's the main admin user, prevent removal
                if (adminCount <= 1 && user.Login == "admin")
                {
                    throw new Exception("Cannot remove the last administrator role.");
                }
            }
        }

        // Find the user-role assignment
        var userRole = ctx.Db.UserRole.Iter()
            .FirstOrDefault(ur => ur.UserId == userId && ur.RoleId == roleId);

        // Check if the assignment exists
        if (userRole == null)
            throw new Exception("User does not have this role.");

        // Delete the user-role assignment
        ctx.Db.UserRole.Id.Delete(userRole.Id);
        // Log the removal
        Log.Info($"Role {roleId} removed from user {userId} by {ctx.Sender}");
    }

    // Helper method to check if a user has a specific permission
    private static bool HasPermission(ReducerContext ctx, Identity userId, string permissionName)
    {
        // Get all roles for the user
        var roleIds = ctx.Db.UserRole.Iter()
                           .Where(ur => ur.UserId == userId)  // Find all role assignments for this user
                           .Select(ur => ur.RoleId)           // Get the role IDs
                           .ToList();                         // Convert to list

        // Check if any of the user's roles have the specified permission
        var permissionIds = ctx.Db.RolePermission.Iter()
                                .Where(rp => roleIds.Contains(rp.RoleId))  // Find permissions for user's roles
                                .Select(rp => rp.PermissionId)             // Get the permission IDs
                                .ToList();                                 // Convert to list

        // Final permission check - look for the specific permission name among the user's permissions
        return ctx.Db.Permission.Iter()
                    .Where(p => permissionIds.Contains(p.PermissionId))  // Filter to user's permissions
                    .Any(p => p.Name == permissionName && p.IsActive);   // Check for matching name and active status
    }

    [SpacetimeDB.Reducer]
    public static void CreateBus(ReducerContext ctx, string model, string? registrationNumber)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "create_bus"))
        {
            throw new Exception("Unauthorized: Missing create_bus permission");
        }

        // Get the next bus ID from the counter
        uint busId = 0;
        var counter = ctx.Db.BusIdCounter.Key.Find("busId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.BusIdCounter.Insert(new BusIdCounter { Key = "busId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.BusIdCounter.Key.Update(counter);
        }
        busId = counter.NextId;  // Use the counter value

        // Create a new bus with the provided information
        var bus = new Bus
        {
            BusId = busId,                       // Set the bus ID
            Model = model,                       // Set the model
            RegistrationNumber = registrationNumber,  // Set the registration number (can be null)
            IsActive = true                      // Set as active by default
        };
        // Insert the new bus into the database
        ctx.Db.Bus.Insert(bus);
    }

    [SpacetimeDB.Reducer]
    public static void CreateMaintenance(ReducerContext ctx, uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "schedule_maintenance"))
        {
            throw new Exception("Unauthorized: Missing schedule_maintenance permission");
        }
        // Validate that the bus exists
        if (!ctx.Db.Bus.Iter().Any(b => b.BusId == busId))
        {
            throw new Exception("Bus not found");
        }

        // Get the next maintenance ID from the counter
        uint maintenanceId = 0;
        var counter = ctx.Db.MaintenanceIdCounter.Key.Find("maintenanceId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.MaintenanceIdCounter.Insert(new MaintenanceIdCounter { Key = "maintenanceId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.MaintenanceIdCounter.Key.Update(counter);
        }
        maintenanceId = counter.NextId;  // Use the counter value

        // Create a new maintenance record with the provided information
        var maintenance = new Maintenance
        {
            MaintenanceId = maintenanceId,       // Set the maintenance ID
            BusId = busId,                       // Set the bus ID
            LastServiceDate = lastServiceDate,   // Set the last service date
            NextServiceDate = nextServiceDate,   // Set the next service date
            ServiceEngineer = serviceEngineer,   // Set the service engineer
            FoundIssues = foundIssues,           // Set the found issues
            Roadworthiness = roadworthiness,     // Set the roadworthiness status
            MaintenanceType = maintenanceType    // Set the maintenance type
        };
        // Insert the new maintenance record into the database
        ctx.Db.Maintenance.Insert(maintenance);
    }

    [SpacetimeDB.Reducer]
    public static void CreateRoute(ReducerContext ctx, string startPoint, string endPoint, uint driverId, uint busId, string? travelTime = null, bool isActive = true)
    {
        // Validate that the employee (driver) exists
        if (!ctx.Db.Employee.Iter().Any(e => e.EmployeeId == driverId))
        {
            throw new Exception("Driver not found.");
        }
        // Validate that the bus exists
        if (!ctx.Db.Bus.Iter().Any(b => b.BusId == busId))
        {
            throw new Exception("Bus not found.");
        }

        // Get the next route ID from the counter
        uint routeId = 0;
        var counter = ctx.Db.RouteIdCounter.Key.Find("routeId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.RouteIdCounter.Insert(new RouteIdCounter { Key = "routeId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.RouteIdCounter.Key.Update(counter);
        }
        routeId = counter.NextId;  // Use the counter value

        // Create a new route with the provided information
        var route = new Route
        {
            RouteId = routeId,           // Set the route ID
            StartPoint = startPoint,     // Set the start point
            EndPoint = endPoint,         // Set the end point
            DriverId = driverId,         // Set the driver ID
            BusId = busId,               // Set the bus ID
            TravelTime = travelTime,     // Set the travel time
            IsActive = isActive          // Set as active based on parameter
        };
        // Insert the new route into the database
        ctx.Db.Route.Insert(route);
    }

    [SpacetimeDB.Reducer]
    public static void CreateRouteSchedule(ReducerContext ctx,
        uint routeId,
        ulong departureTime,
        double price,
        uint availableSeats,
        string[]? daysOfWeek = null,
        string? startPoint = null,
        string? endPoint = null,
        string[]? routeStops = null,
        ulong? arrivalTime = null,
        uint? stopDurationMinutes = null,
        bool? isRecurring = null,
        string[]? estimatedStopTimes = null,
        double[]? stopDistances = null,
        string? notes = null)
    {
        // Validate that the route exists
        if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
        {
            throw new Exception("Route not found.");
        }

        // Get the next schedule ID from the counter
        uint scheduleId = 0;
        var counter = ctx.Db.ScheduleIdCounter.Key.Find("scheduleId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.ScheduleIdCounter.Insert(new ScheduleIdCounter { Key = "scheduleId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.ScheduleIdCounter.Key.Update(counter);
        }
        scheduleId = counter.NextId;  // Use the counter value

        // Create a new route schedule with the provided information
        var schedule = new RouteSchedule
        {
            ScheduleId = scheduleId,                 // Set the schedule ID
            RouteId = routeId,                       // Set the route ID
            DepartureTime = departureTime,           // Set the departure time
            ArrivalTime = arrivalTime ?? 0,         // Set the arrival time (default to 0 if not provided)
            Price = price,                           // Set the price
            AvailableSeats = availableSeats,         // Set the available seats
            DaysOfWeek = daysOfWeek,                 // Set the days of week
            StartPoint = startPoint,                 // Set the start point
            EndPoint = endPoint,                     // Set the end point
            RouteStops = routeStops,                 // Set the route stops
            StopDurationMinutes = stopDurationMinutes, // Set the stop duration
            IsRecurring = isRecurring ?? false,      // Set recurring status (default to false if not provided)
            EstimatedStopTimes = estimatedStopTimes, // Set estimated stop times
            StopDistances = stopDistances,           // Set stop distances
            Notes = notes,                           // Set notes
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set creation time
            UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set update time
            UpdatedBy = ctx.Sender.ToString(),       // Set updater
            IsActive = true,                         // Set as active by default
            ValidFrom = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000   // Set valid from date
        };
        // Insert the new schedule into the database
        ctx.Db.RouteSchedule.Insert(schedule);
    }

    [SpacetimeDB.Reducer]
    public static void CreateEmployee(ReducerContext ctx, string employeeName, string employeeSurname, string employeePatronym, uint jobId)
    {
        // Validate that the job exists
        if (!ctx.Db.Job.Iter().Any(r => r.JobId == jobId))
        {
            throw new Exception("Job not found");
        }

        // Get the next employee ID from the counter
        uint employeeId = 0;
        var counter = ctx.Db.EmployeeIdCounter.Key.Find("employeeId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.EmployeeIdCounter.Insert(new EmployeeIdCounter { Key = "employeeId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.EmployeeIdCounter.Key.Update(counter);
        }
        employeeId = counter.NextId;  // Use the counter value

        // Create a new employee with the provided information
        var employee = new Employee
        {
            EmployeeId = employeeId,             // Set the employee ID
            Name = employeeName,                 // Set the name
            Surname = employeeSurname,           // Set the surname
            Patronym = employeePatronym,         // Set the patronym
            JobId = jobId,                       // Set the job ID
            EmployedSince = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000  // Set employment date
        };
        // Insert the new employee into the database
        ctx.Db.Employee.Insert(employee);
    }

    [SpacetimeDB.Reducer]
    public static void CreateJob(ReducerContext ctx, string jobTitle, string jobInternship)
    {
        // Get the next job ID from the counter
        uint jobId = 0;
        var counter = ctx.Db.JobIdCounter.Key.Find("jobId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.JobIdCounter.Insert(new JobIdCounter { Key = "jobId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.JobIdCounter.Key.Update(counter);
        }
        jobId = counter.NextId;  // Use the counter value

        // Create a new job with the provided information
        var job = new Job
        {
            JobId = jobId,               // Set the job ID
            JobTitle = jobTitle,         // Set the job title
            Internship = jobInternship   // Set the internship requirement
        };
        // Insert the new job into the database
        ctx.Db.Job.Insert(job);
    }

    [SpacetimeDB.Reducer]
    public static void CreateTicket(ReducerContext ctx, uint routeId, double price, uint seatNumber, string paymentMethod, ulong? purchaseTime = null)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "create_ticket"))
        {
            throw new Exception("Unauthorized: Missing CREATE_TICKET permission");
        }

        // Validate that the route exists
        if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
        {
            throw new Exception("Route not found");
        }

        // Check if the seat is already taken
        if (ctx.Db.Ticket.Iter().Any(t => t.RouteId == routeId && t.SeatNumber == seatNumber && t.IsActive))
        {
            throw new Exception("Seat is already taken");
        }

        // Get the next ticket ID from the counter
        uint ticketId = 0;
        var counter = ctx.Db.TicketIdCounter.Key.Find("ticketId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.TicketIdCounter.Insert(new TicketIdCounter { Key = "ticketId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.TicketIdCounter.Key.Update(counter);
        }
        ticketId = counter.NextId;  // Use the counter value

        // Create a new ticket with the provided information
        var ticket = new Ticket
        {
            TicketId = ticketId,         // Set the ticket ID
            RouteId = routeId,           // Set the route ID
            TicketPrice = price,         // Set the ticket price
            SeatNumber = seatNumber,     // Set the seat number
            PaymentMethod = paymentMethod, // Set the payment method
            IsActive = true,             // Set the ticket as active
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, // Set creation time
            UpdatedAt = null,            // No updates yet
            UpdatedBy = null,            // No updates yet
            PurchaseTime = purchaseTime ?? (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 // Set purchase time
        };
        // Insert the new ticket into the database
        ctx.Db.Ticket.Insert(ticket);
    }

    [SpacetimeDB.Reducer]
    public static void CreateSale(ReducerContext ctx, uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null)
    {
        // Validate that the ticket exists
        if (!ctx.Db.Ticket.Iter().Any(t => t.TicketId == ticketId))
            throw new Exception("Ticket not found.");

        uint saleId = 0;
        var counter = ctx.Db.SaleIdCounter.Key.Find("saleId");
        if (counter == null)
        {
            counter = ctx.Db.SaleIdCounter.Insert(new SaleIdCounter { Key = "saleId", NextId = 1 });
        }
        else
        {
            counter.NextId++;
            ctx.Db.SaleIdCounter.Key.Update(counter);
        }
        saleId = counter.NextId;

        var sale = new Sale
        {
            SaleId = saleId,
            TicketId = ticketId,
            SaleDate = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            TicketSoldToUser = buyerName,
            TicketSoldToUserPhone = buyerPhone,
            SellerId = ctx.Sender,
            SaleLocation = saleLocation,
            SaleNotes = saleNotes
        };
        ctx.Db.Sale.Insert(sale);
    }

    [SpacetimeDB.Reducer]
    public static void CancelTicket(ReducerContext ctx, uint ticketId)
    {
        var ticket = ctx.Db.Ticket.Iter().FirstOrDefault(t => t.TicketId == ticketId);
        if (ticket == null)
        {
            throw new Exception("Ticket not found");
        }

        // Only ticket owner or admin can cancel
        if (!HasPermission(ctx, ctx.Sender, "cancel_ticket"))
        {
            throw new Exception("Unauthorized: Cannot cancel ticket");
        }

        // Update the ticket to set IsActive to false and record the update details
        ticket.IsActive = false;
        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = ctx.Sender.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);

        Log.Info($"Ticket {ticketId} cancelled by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void CreatePassenger(ReducerContext ctx, string name, string email, string phoneNumber)
    {
        uint passengerId = 0;
        var counter = ctx.Db.PassengerIdCounter.Key.Find("passengerId");
        if (counter == null)
        {
            counter = ctx.Db.PassengerIdCounter.Insert(new PassengerIdCounter { Key = "passengerId", NextId = 1 });
        }
        else
        {
            counter.NextId++;
            ctx.Db.PassengerIdCounter.Key.Update(counter);
        }
        passengerId = counter.NextId;

        var passenger = new Passenger
        {
            PassengerId = passengerId,
            Name = name,
            Email = email,
            PhoneNumber = phoneNumber,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            UpdatedAt = null,
            UpdatedBy = null
        };
        ctx.Db.Passenger.Insert(passenger);
        Log.Info($"Passenger {name} created with ID {passengerId}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdatePassenger(ReducerContext ctx, uint passengerId, string? name, string? email, string? phoneNumber, bool? isActive)
    {
        var passenger = ctx.Db.Passenger.PassengerId.Find(passengerId);
        if (passenger == null)
        {
            throw new Exception("Passenger not found.");
        }

        if (name != null)
        {
            passenger.Name = name;
        }
        if (email != null)
        {
            passenger.Email = email;
        }
        if (phoneNumber != null)
        {
            passenger.PhoneNumber = phoneNumber;
        }
        if (isActive.HasValue)
        {
            passenger.IsActive = isActive.Value;
        }

        passenger.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        passenger.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.Passenger.PassengerId.Update(passenger);
        Log.Info($"Passenger {passengerId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeletePassenger(ReducerContext ctx, uint passengerId)
    {
        var passenger = ctx.Db.Passenger.PassengerId.Find(passengerId);
        if (passenger == null)
        {
            throw new Exception("Passenger not found.");
        }

        ctx.Db.Passenger.PassengerId.Delete(passengerId);
        Log.Info($"Passenger {passengerId} has been deleted.");
    }

	[SpacetimeDB.Reducer]
	public static void Add(ReducerContext ctx, string name, int age)
	{
        var person = ctx.Db.Person.Insert(new Person { Name = name, Age = age });
        Log.Info($"Inserted {person.Name} under #{person.Id}");
	}

	[SpacetimeDB.Reducer]
	public static void SayHello(ReducerContext ctx)
	{
        foreach (var person in ctx.Db.Person.Iter())
        {
            Log.Info($"Hello, {person.Name}!");
        }
		Log.Info("Hello, World!");
	}

    [SpacetimeDB.Reducer]
    public static void ClaimUserAccount(ReducerContext ctx, string login, string password)
    {
        // Find the user by login
        var user = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == login);
        if (user == null)
        {
            Log.Error($"Attempt to claim non-existent account: {login}");
            return;
        }
        
        // Verify password using the VerifyPassword method
        if (!VerifyPassword(password, user.PasswordHash))
        {
            Log.Error($"Invalid password for account claim: {login}");
            return;
        }
        
        if (user.IsActive && user.UserId != ctx.Sender)
        {
            Log.Error($"Account {login} is already claimed by another identity");
            return;
        }
        
        // Update the user with the caller's identity
        user.UserId = ctx.Sender;
        user.IsActive = true;
        user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.Login.Update(user);
        
        // Update user roles to reference the new identity
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.UserId == user.UserId).ToList();
        foreach (var role in userRoles)
        {
            role.UserId = ctx.Sender;
            ctx.Db.UserRole.Id.Update(role);
        }
        
        Log.Info($"User {login} successfully claimed by identity {ctx.Sender}");
	}

    private static void InitializeJobs(ReducerContext ctx)
    {
        // Check if jobs already exist
        if (ctx.Db.Job.Iter().Any())
        {
            return; // Jobs already exist
        }

        Log.Info("Initializing jobs...");
        
        var jobs = new[]
        {
            ("Водитель автобуса", "Стажировка (2 года)"),
            ("Механик", "Стажировка (3 года)"),
            ("Диспетчер", "Стажировка (1 год)"),
            ("Начальник автопарка", "Стажировка (5 лет)"),
            ("Кассир", "Стажировка (6 месяцев)"),
            ("Инженер по безопасности", "Стажировка (3 года)"),
            ("Автоэлектрик", "Стажировка (2 года)"),
            ("Мойщик автобусов", "Стажировка (1 месяц)"),
            ("Сменный мастер", "Стажировка (4 года)"),
            ("Контролер", "Стажировка (1 год)")
        };

        foreach (var (title, internship) in jobs)
        {
            uint jobId = GetNextId(ctx, "jobId");
            var job = new Job
            {
                JobId = jobId,
                JobTitle = title,
                Internship = internship
            };
            ctx.Db.Job.Insert(job);
        }
        
        Log.Info("Jobs initialized successfully");
    }

    private static void InitializeEmployees(ReducerContext ctx)
    {
        // Check if employees already exist
        if (ctx.Db.Employee.Iter().Any())
        {
            return; // Employees already exist
        }

        Log.Info("Initializing employees...");
        
        // Get job IDs
        var jobs = ctx.Db.Job.Iter().ToList();
        if (jobs.Count == 0)
        {
            Log.Error("Cannot initialize employees: No jobs found");
            return;
        }
        
        var employees = new[]
        {
            ("Иванов", "Иван", "Иванович", new DateTime(2020, 1, 15), 0), // Driver
            ("Петров", "Петр", "Петрович", new DateTime(2019, 3, 20), 0), // Driver
            ("Сидоров", "Алексей", "Михайлович", new DateTime(2018, 6, 10), 1), // Mechanic
            ("Козлов", "Дмитрий", "Сергеевич", new DateTime(2021, 2, 5), 2), // Dispatcher
            ("Морозов", "Андрей", "Владимирович", new DateTime(2017, 8, 25), 3), // Park Manager
            ("Новиков", "Сергей", "Александрович", new DateTime(2022, 4, 12), 4), // Cashier
            ("Волков", "Михаил", "Дмитриевич", new DateTime(2020, 11, 30), 0), // Driver
            ("Соловьев", "Артем", "Игоревич", new DateTime(2019, 9, 15), 1), // Mechanic
            ("Васильев", "Николай", "Андреевич", new DateTime(2021, 7, 8), 0), // Driver
            ("Зайцев", "Владимир", "Петрович", new DateTime(2018, 12, 3), 2)  // Dispatcher
        };

        foreach (var (surname, name, patronym, employedSince, jobIndex) in employees)
        {
            uint employeeId = GetNextId(ctx, "employeeId");
            var jobId = jobs[jobIndex % jobs.Count].JobId;
            
            // Fix: Convert DateTime to Unix timestamp (milliseconds) safely
            ulong employedSinceMs = (ulong)((DateTimeOffset)employedSince).ToUnixTimeMilliseconds();
            
            var employee = new Employee
            {
                EmployeeId = employeeId,
                Surname = surname,
                Name = name,
                Patronym = patronym,
                EmployedSince = employedSinceMs,
                JobId = jobId
            };
            ctx.Db.Employee.Insert(employee);
        }
        
        Log.Info("Employees initialized successfully");
    }

    private static void InitializeBuses(ReducerContext ctx)
    {
        // Check if buses already exist
        if (ctx.Db.Bus.Iter().Any())
        {
            return; // Buses already exist
        }

        Log.Info("Initializing buses...");
        
        var buses = new[]
        {
            "МАЗ-203.069",
            "МАЗ-215.069",
            "МАЗ-107.468",
            "МАЗ-103.065",
            "МАЗ-203.169",
            "МАЗ-105.065",
            "МАЗ-203.L65",
            "МАЗ-206.068",
            "МАЗ-103.465",
            "МАЗ-107.066"
        };

        foreach (var model in buses)
        {
            uint busId = GetNextId(ctx, "busId");
            var bus = new Bus
            {
                BusId = busId,
                Model = model,
                RegistrationNumber = $"AB {busId + 1000} 7",
                IsActive = true
            };
            ctx.Db.Bus.Insert(bus);
        }
        
        Log.Info("Buses initialized successfully");
    }

    private static void InitializeRoutes(ReducerContext ctx)
    {
        // Check if routes already exist
        if (ctx.Db.Route.Iter().Any())
        {
            return; // Routes already exist
        }

        Log.Info("Initializing routes...");
        
        // Get drivers (employees with job title "Водитель автобуса")
        var driverJob = ctx.Db.Job.Iter().FirstOrDefault(j => j.JobTitle == "Водитель автобуса");
        if (driverJob == null)
        {
            Log.Error("Cannot initialize routes: Driver job not found");
            return;
        }
        
        var drivers = ctx.Db.Employee.Iter()
            .Where(e => e.JobId == driverJob.JobId)
            .ToList();
        
        if (drivers.Count == 0)
        {
            // Fallback to any employee if no drivers found
            drivers = ctx.Db.Employee.Iter().ToList();
        }
        
        // Get buses
        var buses = ctx.Db.Bus.Iter().ToList();
        if (buses.Count == 0)
        {
            Log.Error("Cannot initialize routes: No buses found");
            return;
        }
        
        var routes = new[]
        {
            ("Вейнянка", "Фатина", "45 минут"),
            ("Мал. Боровка", "Солтановка", "50 минут"),
            ("Вокзал", "Спутник", "40 минут"),
            ("Мясокомбинат", "Заводская", "35 минут"),
            ("Броды", "Казимировка", "55 минут"),
            ("Гребеневский рынок", "Холмы", "45 минут"),
            ("Автовокзал", "Полыковичи", "40 минут"),
            ("Центр", "Сидоровичи", "60 минут"),
            ("Площадь Славы", "Буйничи", "30 минут"),
            ("Заднепровье", "Химволокно", "25 минут"),
            ("Вокзал", "Соломинка", "35 минут"),
            ("Площадь Ленина", "Чаусы", "50 минут"),
            ("Могилев-2", "Дашковка", "40 минут"),
            ("Кожзавод", "Сухари", "45 минут"),
            ("Гребеневский рынок", "Любуж", "30 минут")
        };

        for (int i = 0; i < routes.Length; i++)
        {
            var (startPoint, endPoint, travelTime) = routes[i];
            uint routeId = GetNextId(ctx, "routeId");
            
            var route = new Route
            {
                RouteId = routeId,
                StartPoint = startPoint,
                EndPoint = endPoint,
                DriverId = drivers[i % drivers.Count].EmployeeId,
                BusId = buses[i % buses.Count].BusId,
                TravelTime = travelTime,
                IsActive = true
            };
            ctx.Db.Route.Insert(route);
        }
        
        Log.Info("Routes initialized successfully");
    }

    private static void InitializeTickets(ReducerContext ctx)
    {
        // Check if tickets already exist
        if (ctx.Db.Ticket.Iter().Any())
        {
            return; // Tickets already exist
        }

        Log.Info("Initializing tickets...");
        
        // Get routes
        var routes = ctx.Db.Route.Iter().ToList();
        if (routes.Count == 0)
        {
            Log.Error("Cannot initialize tickets: No routes found");
            return;
        }
        
        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Create tickets for each route with realistic prices
        foreach (var route in routes)
        {
            uint ticketId = GetNextId(ctx, "ticketId");
            
            var ticket = new Ticket
            {
                TicketId = ticketId,
                RouteId = route.RouteId,
                TicketPrice = 0.75 + (route.RouteId % 3) * 0.10, // Prices between 0.75 and 0.95
                SeatNumber = 1, // Default seat number
                PaymentMethod = "cash", // Default payment method
                IsActive = true, // Set ticket as active
                CreatedAt = now,
                UpdatedAt = null,
                UpdatedBy = null,
                PurchaseTime = now // Set purchase time to current time
            };
            ctx.Db.Ticket.Insert(ticket);
        }
        
        Log.Info("Tickets initialized successfully");
    }

    private static void InitializeMaintenance(ReducerContext ctx)
    {
        // Check if maintenance records already exist
        if (ctx.Db.Maintenance.Iter().Any())
        {
            return; // Maintenance records already exist
        }

        Log.Info("Initializing maintenance records...");
        
        // Get buses
        var buses = ctx.Db.Bus.Iter().ToList();
        if (buses.Count == 0)
        {
            Log.Error("Cannot initialize maintenance: No buses found");
            return;
        }
        
        var maintenanceTypes = new[]
        {
            ("Замена масла, фильтров", "Закончилось масло, грязные фильтры", "Исправен"),
            ("Регулировка тормозов", "Тормоза", "Исправен"),
            ("Замена тормозных колодок", "Тормозные колодки", "Исправен"),
            ("Диагностика двигателя", "Диагностика двигателя", "Требует внимания"),
            ("Плановый осмотр", "Плановый осмотр", "Исправен"),
            ("Замена ремня ГРМ", "Ремень ГРМ", "Исправен"),
            ("Ремонт системы охлаждения", "Ремонт системы охлаждения", "Исправен"),
            ("Замена аккумулятора", "Аккумулятор", "Исправен"),
            ("Диагностика электрики", "Диагностика электрики", "Требует внимания"),
            ("Плановое ТО", "Плановое ТО", "Исправен")
        };
        
        var engineers = new[] { "Сидоров А.М.", "Соловьев А.И." };
        
        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        for (int i = 0; i < buses.Count; i++)
        {
            uint maintenanceId = GetNextId(ctx, "maintenanceId");
            var (maintenanceType, foundIssues, roadworthiness) = maintenanceTypes[i % maintenanceTypes.Length];
            
            // Random dates within the last 2 months
            var daysAgo = (i * 5) % 60;
            ulong lastServiceDate = now - (ulong)(daysAgo * 24 * 60 * 60 * 1000);
            ulong nextServiceDate = now + (ulong)((90 - daysAgo) * 24 * 60 * 60 * 1000); // 3 months after last service
            
            var maintenance = new Maintenance
            {
                MaintenanceId = maintenanceId,
                BusId = buses[i].BusId,
                LastServiceDate = lastServiceDate,
                NextServiceDate = nextServiceDate,
                ServiceEngineer = engineers[i % engineers.Length],
                FoundIssues = foundIssues,
                Roadworthiness = roadworthiness,
                MaintenanceType = maintenanceType,
                MileageThreshold = "100000 km"
            };
            ctx.Db.Maintenance.Insert(maintenance);
        }
        
        Log.Info("Maintenance records initialized successfully");
    }

    private static void InitializeRouteSchedules(ReducerContext ctx)
    {
        // Check if route schedules already exist
        if (ctx.Db.RouteSchedule.Iter().Any())
        {
            return; // Route schedules already exist
        }

        Log.Info("Initializing route schedules...");
        
        // Get routes
        var routes = ctx.Db.Route.Iter().ToList();
        if (routes.Count == 0)
        {
            Log.Error("Cannot initialize route schedules: No routes found");
            return;
        }
        
        // Bus types
        var busTypes = new[] { "МАЗ-103", "МАЗ-107", "МАЗ-215", "МАЗ-231" };
        
        // Days of week
        var weekdays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
        var weekend = new[] { "Saturday", "Sunday" };
        var allDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        
        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Calculate time offsets safely
        ulong hoursInMs = 60 * 60 * 1000UL;
        ulong daysInMs = 24 * hoursInMs;
        
        ulong thirtyDaysAgo = now - (30 * daysInMs);
        ulong sixtyDaysAhead = now + (60 * daysInMs);
        
        foreach (var route in routes)
        {
            // Create morning schedule (6:00 AM)
            uint morningScheduleId = GetNextId(ctx, "scheduleId");
            var morningSchedule = new RouteSchedule
            {
                ScheduleId = morningScheduleId,
                RouteId = route.RouteId,
                StartPoint = route.StartPoint,
                EndPoint = route.EndPoint,
                RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint },
                DepartureTime = now - (8 * hoursInMs), // 8 hours ago
                ArrivalTime = now - (7 * hoursInMs),   // 7 hours ago
                Price = 0.75 + (route.RouteId % 3) * 0.10,         // Match ticket prices
                AvailableSeats = 42,
                DaysOfWeek = allDays,
                BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] },
                IsActive = true,
                ValidFrom = thirtyDaysAgo,
                ValidUntil = sixtyDaysAhead,
                StopDurationMinutes = 5,
                IsRecurring = true,
                EstimatedStopTimes = new[] { "06:00", "06:15", "06:30", "06:45" },
                StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 },
                Notes = $"Утренний рейс {route.StartPoint} - {route.EndPoint}",
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "System"
            };
            ctx.Db.RouteSchedule.Insert(morningSchedule);
            
            // Create afternoon schedule (2:00 PM) - weekdays only
            uint afternoonScheduleId = GetNextId(ctx, "scheduleId");
            var afternoonSchedule = new RouteSchedule
            {
                ScheduleId = afternoonScheduleId,
                RouteId = route.RouteId,
                StartPoint = route.StartPoint,
                EndPoint = route.EndPoint,
                RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint },
                DepartureTime = now - (2 * hoursInMs), // 2 hours ago
                ArrivalTime = now - (1 * hoursInMs),   // 1 hour ago
                Price = 0.75 + (route.RouteId % 3) * 0.10,         // Match ticket prices
                AvailableSeats = 42,
                DaysOfWeek = weekdays,
                BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] },
                IsActive = true,
                ValidFrom = thirtyDaysAgo,
                ValidUntil = sixtyDaysAhead,
                StopDurationMinutes = 5,
                IsRecurring = true,
                EstimatedStopTimes = new[] { "14:00", "14:15", "14:30", "14:45" },
                StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 },
                Notes = $"Дневной рейс {route.StartPoint} - {route.EndPoint}",
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "System"
            };
            ctx.Db.RouteSchedule.Insert(afternoonSchedule);
            
            // Create evening schedule (6:00 PM)
            uint eveningScheduleId = GetNextId(ctx, "scheduleId");
            var eveningSchedule = new RouteSchedule
            {
                ScheduleId = eveningScheduleId,
                RouteId = route.RouteId,
                StartPoint = route.StartPoint,
                EndPoint = route.EndPoint,
                RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint },
                DepartureTime = now + (4 * hoursInMs), // 4 hours from now
                ArrivalTime = now + (5 * hoursInMs),   // 5 hours from now
                Price = 0.75 + (route.RouteId % 3) * 0.10,         // Match ticket prices
                AvailableSeats = 42,
                DaysOfWeek = allDays,
                BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] },
                IsActive = true,
                ValidFrom = thirtyDaysAgo,
                ValidUntil = sixtyDaysAhead,
                StopDurationMinutes = 5,
                IsRecurring = true,
                EstimatedStopTimes = new[] { "18:00", "18:15", "18:30", "18:45" },
                StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 },
                Notes = $"Вечерний рейс {route.StartPoint} - {route.EndPoint}",
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "System"
            };
            ctx.Db.RouteSchedule.Insert(eveningSchedule);
        }
        
        Log.Info("Route schedules initialized successfully");
    }

    private static void InitializeSales(ReducerContext ctx)
    {
        // Check if sales already exist
        if (ctx.Db.Sale.Iter().Any())
        {
            return; // Sales already exist
        }

        Log.Info("Initializing sales...");
        
        // Get tickets
        var tickets = ctx.Db.Ticket.Iter().ToList();
        if (tickets.Count == 0)
        {
            Log.Error("Cannot initialize sales: No tickets found");
            return;
        }
        
        // Get admin user
        var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin");
        var guestUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "guest");
        
        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Calculate time constants safely
        ulong hoursInMs = 60 * 60 * 1000UL;
        ulong daysInMs = 24 * hoursInMs;
        ulong monthInMs = 30 * daysInMs;
        
        // Create historical sales (last 6 months)
        for (int month = 6; month >= 0; month--)
        {
            for (int day = 1; day <= 5; day++)
            {
                // Skip some days randomly
                if (day % 3 == 0 && month % 2 == 0) continue;
                
                for (int i = 0; i < 3; i++)
                {
                    uint saleId = GetNextId(ctx, "saleId");
                    var ticketIndex = (month * day + i) % tickets.Count;
                    
                    // Calculate sale date (X months and Y days ago)
                    ulong saleDate = now - ((ulong)month * monthInMs + (ulong)day * daysInMs);
                    
                    var sale = new Sale
                    {
                        SaleId = saleId,
                        TicketId = tickets[ticketIndex].TicketId,
                        SaleDate = saleDate,
                        TicketSoldToUser = "Физическая продажа",
                        TicketSoldToUserPhone = "", // No phone number for physical sales
                        SellerId = (month < 1 && i % 2 == 0) ? adminUser?.UserId : null, // Recent sales by admin
                        SaleLocation = "В автобусе", // Sale made inside the bus
                        SaleNotes = "Продажа билета физически"
                    };
                    ctx.Db.Sale.Insert(sale);
                }
            }
        }
        
        // Create recent sales (last few days) with admin and guest users
        if (adminUser != null && guestUser != null)
        {
            for (int day = 5; day >= 0; day--)
            {
                uint saleId = GetNextId(ctx, "saleId");
                var ticketIndex = day % tickets.Count;
                
                // Calculate sale date (X days ago)
                ulong saleDate = now - ((ulong)day * daysInMs);
                
                var sale = new Sale
                {
                    SaleId = saleId,
                    TicketId = tickets[ticketIndex].TicketId,
                    SaleDate = saleDate,
                    TicketSoldToUser = day % 2 == 0 ? "admin" : "guest",
                    TicketSoldToUserPhone = "+375291234567",
                    SellerId = day % 2 == 0 ? adminUser.UserId : guestUser.UserId,
                    SaleLocation = "Онлайн", // Online sale
                    SaleNotes = "Продажа билета онлайн"
                };
                ctx.Db.Sale.Insert(sale);
            }
        }
        
        Log.Info("Sales initialized successfully");
    }

    // ---------- Reducers ----------
    
    [SpacetimeDB.Reducer]
    public static void AddNewPermission(ReducerContext ctx, string name, string description, string category)
    {
        // Check if the caller has the necessary permission (e.g., "permissions.create")
        if (!HasPermission(ctx, ctx.Sender, "permissions.create"))
        {
            throw new Exception("Unauthorized: You do not have permission to create permissions.");
        }

        // Check if a permission with the same name already exists
        if (ctx.Db.Permission.Iter().Any(p => p.Name == name))
        {
            throw new Exception($"A permission with the name '{name}' already exists.");
        }

        uint permissionId = GetNextId(ctx, "permissionId");
        var permission = new Permission
        {
            PermissionId = permissionId,
            Name = name,
            Description = description,
            Category = category,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        ctx.Db.Permission.Insert(permission);
        Log.Info($"Created new permission: {permission.Name} ({permission.PermissionId})");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateUser(ReducerContext ctx, Identity userId, string? login, string? passwordHash,
        int? role, string? phoneNumber, string? email, bool? isActive)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "users.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit users.");
        }
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            throw new Exception("User not found.");
        }
        // Update only if new value is not null
        if (login != null)
        {
            // Check if the new login is already taken by another user
            if (ctx.Db.UserProfile.Iter().Any(u => u.Login == login && !u.UserId.Equals(userId))) // Use Equals for Identity comparison
            {
                throw new Exception("Login already in use by another user.");
            }
            user.Login = login;
        }
        if (passwordHash != null)
        {
           user.PasswordHash = HashPassword(passwordHash);
        }
        if (email != null)
        {
            user.Email = email;
        }
        if (phoneNumber != null)
        {
            user.PhoneNumber = phoneNumber;
        }
        if (isActive.HasValue)
        {
            user.IsActive = isActive.Value;
        }
        // Update LastLoginAt to the current timestamp if set to a different time
        user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} updated");

        // Handle role updates, Use the assignRole and removeRole for this, first check if it has the role,
        // then assign or remove, depending on the new role.
        if (role.HasValue)
        {
            // Find role by legacy ID
            var roleEntity = ctx.Db.Role.Iter()
                .FirstOrDefault(r => r.LegacyRoleId == role.Value);

            if (roleEntity != null)
            {
                // Check if the user already has this role
                var existingUserRole = ctx.Db.UserRole.Iter()
                    .FirstOrDefault(ur => ur.UserId.Equals(userId) && ur.RoleId == roleEntity.RoleId);

                if (existingUserRole == null)
                {
                    // Assign the new role if not already assigned
                    AssignRole(ctx, userId, roleEntity.RoleId);
                }
            }
        }
    }

    [SpacetimeDB.Reducer]
    public static void ActivateUser(ReducerContext ctx, Identity userId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "users.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to activate users.");
        }
        
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            throw new Exception("User not found.");
        }
        
        if (user.IsActive)
        {
            // User is already active, just log and return
            Log.Info($"User {userId} is already active");
            return;
        }
        
        user.IsActive = true;
        ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} activated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateUser(ReducerContext ctx, Identity userId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "users.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to deactivate users.");
        }
        
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            throw new Exception("User not found.");
        }
        
        // Prevent deactivating the last admin
        if (user.Login == "admin")
        {
            var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole != null)
            {
                var adminCount = ctx.Db.UserRole.Iter()
                    .Where(ur => ur.RoleId == adminRole.RoleId)
                    .Join(ctx.Db.UserProfile.Iter(), ur => ur.UserId, u => u.UserId, (ur, u) => u)
                    .Where(u => u.IsActive)
                    .Count();
                
                if (adminCount <= 1)
                {
                    throw new Exception("Cannot deactivate the last active administrator.");
                }
            }
        }
        
        if (!user.IsActive)
        {
            // User is already inactive, just log and return
            Log.Info($"User {userId} is already inactive");
            return;
        }
        
        user.IsActive = false;
        ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} deactivated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void ChangePassword(ReducerContext ctx, Identity userId, string currentPassword, string newPassword)
    {
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            throw new Exception("User not found.");
        }
        
        // Check if this is the user changing their own password or an admin
        bool isAdmin = HasPermission(ctx, ctx.Sender, "users.edit");
        bool isSelf = ctx.Sender.Equals(userId);
        
        if (!isAdmin && !isSelf)
        {
            throw new Exception("Unauthorized: You can only change your own password unless you have admin privileges.");
        }
        
        // If it's the user changing their own password, verify the current password
        if (isSelf && !isAdmin)
        {
            string hashedCurrentPassword = HashPassword(currentPassword);
            if (user.PasswordHash != hashedCurrentPassword)
            {
                throw new Exception("Current password is incorrect.");
            }
        }
        
        // Validate new password (you might want to add more validation rules)
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
        {
            throw new Exception("New password must be at least 6 characters long.");
        }
        
        // Update the password
        user.PasswordHash = HashPassword(newPassword);
        ctx.Db.UserProfile.UserId.Update(user);
        
        Log.Info($"Password changed for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteUser(ReducerContext ctx, Identity userId)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.delete"))
        {
            throw new Exception("Unauthorized: Missing users.delete permission");
        }
        // Prevent deleting the last admin (check against hardcoded admin login)
        var userToDelete = ctx.Db.UserProfile.UserId.Find(userId);
        if (userToDelete != null && userToDelete.Login == "admin")
        {
            var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole != null)
            {
                var adminCount = ctx.Db.UserRole.Iter()
                    .Where(ur => ur.RoleId == adminRole.RoleId)
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .Count();

                if (adminCount <= 1)
                {
                    throw new Exception("Cannot delete the last administrator.");
                }
            }
        }
        // Check if the user exists
        if (userToDelete == null)
        {
            throw new Exception("User not found.");
        }

        // Remove all role assignments for this user.
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId)).ToList();
        foreach (var userRole in userRoles)
        {
            ctx.Db.UserRole.Id.Delete(userRole.Id); // Delete by the unique ID
        }

        // Delete the user.
        ctx.Db.UserProfile.UserId.Delete(userId);
        Log.Info($"User {userId} has been deleted, with all associated roles removed.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateBus(ReducerContext ctx, uint busId, string? model, string? registrationNumber)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "buses.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit buses.");
        }

        var bus = ctx.Db.Bus.BusId.Find(busId);
        if (bus == null)
        {
            throw new Exception("Bus not found.");
        }

        // Update only if new value is not null
        if (model != null)
        {
            bus.Model = model;
        }
        if (registrationNumber != null)
        {
            bus.RegistrationNumber = registrationNumber;
        }

        ctx.Db.Bus.BusId.Update(bus);
        Log.Info($"Bus {busId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteBus(ReducerContext ctx, uint busId)
    {
        if (!HasPermission(ctx, ctx.Sender, "buses.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete buses.");
        }
        if (ctx.Db.Bus.BusId.Find(busId) == null)
        {
            throw new Exception("Bus Not found");
        }
        ctx.Db.Bus.BusId.Delete(busId);
        Log.Info($"Bus {busId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void ActivateBus(ReducerContext ctx, uint busId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "buses.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to activate buses.");
        }
        
        var bus = ctx.Db.Bus.BusId.Find(busId);
        if (bus == null)
        {
            throw new Exception("Bus not found.");
        }
        
        if (bus.IsActive)
        {
            // Bus is already active, just log and return
            Log.Info($"Bus {busId} is already active");
            return;
        }
        
        bus.IsActive = true;
        ctx.Db.Bus.BusId.Update(bus);
        Log.Info($"Bus {busId} activated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateBus(ReducerContext ctx, uint busId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "buses.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to deactivate buses.");
        }
        
        var bus = ctx.Db.Bus.BusId.Find(busId);
        if (bus == null)
        {
            throw new Exception("Bus not found.");
        }
        
        // Check if the bus is used in any active routes
        var activeRoutes = ctx.Db.Route.Iter()
            .Where(r => r.BusId == busId && r.IsActive)
            .ToList();
            
        if (activeRoutes.Count > 0)
        {
            throw new Exception($"Cannot deactivate bus: it is used in {activeRoutes.Count} active routes. Deactivate the routes first.");
        }
        
        if (!bus.IsActive)
        {
            // Bus is already inactive, just log and return
            Log.Info($"Bus {busId} is already inactive");
            return;
        }
        
        bus.IsActive = false;
        ctx.Db.Bus.BusId.Update(bus);
        Log.Info($"Bus {busId} deactivated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void GetBusMaintenanceHistory(ReducerContext ctx, uint busId)
    {
        // This is a query reducer that doesn't modify state but returns data
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "maintenance.view"))
        {
            throw new Exception("Unauthorized: You do not have permission to view maintenance records.");
        }
        
        var bus = ctx.Db.Bus.BusId.Find(busId);
        if (bus == null)
        {
            throw new Exception("Bus not found.");
        }
        
        // Get all maintenance records for this bus
        var maintenanceRecords = ctx.Db.Maintenance.Iter()
            .Where(m => m.BusId == busId)
            .OrderByDescending(m => m.LastServiceDate)
            .ToList();
            
        // Log the result (in a real system, this would return data to the client)
        Log.Info($"Found {maintenanceRecords.Count} maintenance records for bus {busId}");
        
        foreach (var record in maintenanceRecords)
        {
            Log.Info($"Maintenance ID: {record.MaintenanceId}, " +
                     $"Date: {record.LastServiceDate}, " +
                     $"Type: {record.MaintenanceType}, " +
                     $"Engineer: {record.ServiceEngineer}, " +
                     $"Issues: {record.FoundIssues}, " +
                     $"Roadworthiness: {record.Roadworthiness}");
        }
    }

    [SpacetimeDB.Reducer]
    public static void UpdateMaintenance(ReducerContext ctx, uint maintenanceId, uint? busId, ulong? lastServiceDate, string? serviceEngineer,
    string? foundIssues, ulong? nextServiceDate, string? roadworthiness, string? maintenanceType, string? mileage)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "maintenance.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit maintenance.");
        }
        var maintenance = ctx.Db.Maintenance.MaintenanceId.Find(maintenanceId);
        if (maintenance == null)
        {
            throw new Exception("Maintenance Record not found.");
        }
        //Update each property if and only if it's not null
        if (busId.HasValue)
        {
            maintenance.BusId = busId.Value;
        }
        if (lastServiceDate.HasValue)
        {
            maintenance.LastServiceDate = lastServiceDate.Value;
        }
        if (nextServiceDate.HasValue)
        {
            maintenance.NextServiceDate = nextServiceDate.Value;
        }
        if (serviceEngineer != null)
        {
            maintenance.ServiceEngineer = serviceEngineer;
        }
        if (foundIssues != null)
        {
            maintenance.FoundIssues = foundIssues;
        }

        if (roadworthiness != null)
        {
            maintenance.Roadworthiness = roadworthiness;
        }
        if (maintenanceType != null)
        {
            maintenance.MaintenanceType = maintenanceType;
        }
        if (mileage != null)
        {
            maintenance.MileageThreshold = mileage;
        }
        ctx.Db.Maintenance.MaintenanceId.Update(maintenance);
        Log.Info($"Maintenance {maintenanceId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteMaintenance(ReducerContext ctx, uint maintenanceId)
    {
        if (!HasPermission(ctx, ctx.Sender, "maintenance.delete"))
        {
            throw new Exception("Unauthorized: Missing maintenance.delete permission");
        }
        if (ctx.Db.Maintenance.MaintenanceId.Find(maintenanceId) == null)
        {
            throw new Exception("Maintenance record not found");
        }
        ctx.Db.Maintenance.MaintenanceId.Delete(maintenanceId);
        Log.Info($"Maintenance {maintenanceId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRoute(ReducerContext ctx, uint routeId, string? startPoint, string? endPoint, uint? driverId, uint? busId, string? travelTime, bool? isActive)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "routes.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit routes.");
        }
        var route = ctx.Db.Route.RouteId.Find(routeId);
        if (route == null)
        {
            throw new Exception("Route not found");
        }

        // Update only if new value is not null
        if (startPoint != null)
        {
            route.StartPoint = startPoint;
        }
        if (endPoint != null)
        {
            route.EndPoint = endPoint;
        }
        if (driverId.HasValue)
        {
            // Validate employee (driver)
            if (!ctx.Db.Employee.Iter().Any(e => e.EmployeeId == driverId))
            {
                throw new Exception("Driver not found.");
            }
            route.DriverId = driverId.Value;
        }
        if (busId.HasValue)
        {
            // Validate bus
            if (!ctx.Db.Bus.Iter().Any(b => b.BusId == busId))
            {
                throw new Exception("Bus not found.");
            }
            route.BusId = busId.Value;
        }
        if (travelTime != null)
        {
            route.TravelTime = travelTime;
        }
        if (isActive.HasValue)
        {
            route.IsActive = isActive.Value;
        }
        ctx.Db.Route.RouteId.Update(route);
        Log.Info($"Route {routeId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRoute(ReducerContext ctx, uint routeId)
    {
        if (!HasPermission(ctx, ctx.Sender, "routes.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete routes.");
        }
        // Check if the route exists
        if (ctx.Db.Route.RouteId.Find(routeId) == null)
        {
            throw new Exception("Route not found.");
        }
        ctx.Db.Route.RouteId.Delete(routeId);
        Log.Info($"Route {routeId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void ActivateRoute(ReducerContext ctx, uint routeId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "routes.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to activate routes.");
        }
        
        var route = ctx.Db.Route.RouteId.Find(routeId);
        if (route == null)
        {
            throw new Exception("Route not found.");
        }
        
        // Check if the bus is active
        var bus = ctx.Db.Bus.BusId.Find(route.BusId);
        if (bus == null || !bus.IsActive)
        {
            throw new Exception("Cannot activate route: the assigned bus is inactive or not found.");
        }
        
        if (route.IsActive)
        {
            // Route is already active, just log and return
            Log.Info($"Route {routeId} is already active");
            return;
        }
        
        route.IsActive = true;
        ctx.Db.Route.RouteId.Update(route);
        Log.Info($"Route {routeId} activated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateRoute(ReducerContext ctx, uint routeId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "routes.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to deactivate routes.");
        }
        
        var route = ctx.Db.Route.RouteId.Find(routeId);
        if (route == null)
        {
            throw new Exception("Route not found.");
        }
        
        // Check if there are active schedules for this route
        var activeSchedules = ctx.Db.RouteSchedule.Iter()
            .Where(s => s.RouteId == routeId && s.IsActive)
            .ToList();
            
        if (activeSchedules.Count > 0)
        {
            throw new Exception($"Cannot deactivate route: it has {activeSchedules.Count} active schedules. Deactivate the schedules first.");
        }
        
        if (!route.IsActive)
        {
            // Route is already inactive, just log and return
            Log.Info($"Route {routeId} is already inactive");
            return;
        }
        
        route.IsActive = false;
        ctx.Db.Route.RouteId.Update(route);
        Log.Info($"Route {routeId} deactivated by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRouteSchedule(ReducerContext ctx, uint scheduleId, uint? routeId, string? startPoint,
        string? endPoint, string[]? routeStops, ulong? departureTime, ulong? arrivalTime,
        double? price, uint? availableSeats, string[]? daysOfWeek, string[]? busTypes, uint? stopDurationMinutes,
        bool? isRecurring, string[]? estimatedStopTimes,
        double[]? stopDistances, string? notes)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "schedules.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit schedules.");
        }
        
        var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId);
        if (schedule == null)
        {
            throw new Exception("Route schedule not found.");
        }

        // Update properties if provided (not null)
        if (routeId.HasValue)
        {
            // Validate that the route exists:
            if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId.Value))
            {
                throw new Exception("Route not found.");
            }
            schedule.RouteId = routeId.Value;
        }
        if (!string.IsNullOrEmpty(startPoint)) schedule.StartPoint = startPoint;
        if (!string.IsNullOrEmpty(endPoint)) schedule.EndPoint = endPoint;
        if (routeStops != null) schedule.RouteStops = routeStops;
        if (departureTime.HasValue) schedule.DepartureTime = departureTime.Value;
        if (arrivalTime.HasValue) schedule.ArrivalTime = arrivalTime.Value;
        if (price.HasValue) schedule.Price = price.Value;
        if (availableSeats.HasValue) schedule.AvailableSeats = availableSeats.Value;
        if (daysOfWeek != null) schedule.DaysOfWeek = daysOfWeek;
        if (busTypes != null) schedule.BusTypes = busTypes;
        if (stopDurationMinutes.HasValue) schedule.StopDurationMinutes = stopDurationMinutes.Value;
        if (isRecurring.HasValue) schedule.IsRecurring = isRecurring.Value;
        if (estimatedStopTimes != null) schedule.EstimatedStopTimes = estimatedStopTimes;
        if (stopDistances != null) schedule.StopDistances = stopDistances;
        if (!string.IsNullOrEmpty(notes)) schedule.Notes = notes;

        // Update the "updated" fields
        schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        schedule.UpdatedBy = ctx.Sender.ToString(); // Or a specific admin user

        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Route schedule {scheduleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRouteSchedule(ReducerContext ctx, uint scheduleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "schedules.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete schedules.");
        }

        // Check if the schedule exists
        if (ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId) == null)
        {
            throw new Exception("Route schedule not found.");
        }
        ctx.Db.RouteSchedule.ScheduleId.Delete(scheduleId);
        Log.Info($"Route schedule {scheduleId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void ActivateSchedule(ReducerContext ctx, uint scheduleId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "schedules.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to activate schedules.");
        }
        
        var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId);
        if (schedule == null)
        {
            throw new Exception("Schedule not found.");
        }
        
        // Check if the route is active
        var route = ctx.Db.Route.RouteId.Find(schedule.RouteId);
        if (route == null || !route.IsActive)
        {
            throw new Exception("Cannot activate schedule: the associated route is inactive or not found.");
        }
        
        if (schedule.IsActive)
        {
            // Schedule is already active, just log and return
            Log.Info($"Schedule {scheduleId} is already active");
            return;
        }
        
        schedule.IsActive = true;
        schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        schedule.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Schedule {scheduleId} activated by {ctx.Sender}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateSchedule(ReducerContext ctx, uint scheduleId)
    {
        // Check for the required permission
        if (!HasPermission(ctx, ctx.Sender, "schedules.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to deactivate schedules.");
        }
        
        var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId);
        if (schedule == null)
        {
            throw new Exception("Schedule not found.");
        }
        
        // Check if there are active sales for this schedule's route
        var route = ctx.Db.Route.RouteId.Find(schedule.RouteId);
        if (route != null)
        {
            var tickets = ctx.Db.Ticket.Iter()
                .Where(t => t.RouteId == route.RouteId)
                .ToList();
                
            if (tickets.Count > 0)
            {
                var ticketIds = tickets.Select(t => t.TicketId).ToList();
                var recentSales = ctx.Db.Sale.Iter()
                    .Where(s => ticketIds.Contains(s.TicketId))
                    .Where(s => s.SaleDate > (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 - 86400000) // Sales in the last 24 hours
                    .ToList();
                    
                if (recentSales.Count > 0)
                {
                    throw new Exception($"Cannot deactivate schedule: there are {recentSales.Count} recent ticket sales for this route. Wait until all tickets are used or refund them first.");
                }
            }
        }
        
        if (!schedule.IsActive)
        {
            // Schedule is already inactive, just log and return
            Log.Info($"Schedule {scheduleId} is already inactive");
            return;
        }
        
        schedule.IsActive = false;
        schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        schedule.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Schedule {scheduleId} deactivated by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateJob(ReducerContext ctx, uint jobId, string? jobTitle, string? internship)
    {
        if (!HasPermission(ctx, ctx.Sender, "jobs.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit jobs.");
        }
        var job = ctx.Db.Job.JobId.Find(jobId);
        if (job == null)
        {
            throw new Exception("Job not found.");
        }

        // Update only if new value is not null
        if (jobTitle != null)
        {
            job.JobTitle = jobTitle;
        }
        if (internship != null)
        {
            job.Internship = internship;
        }

        ctx.Db.Job.JobId.Update(job);
        Log.Info($"Job {jobId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteJob(ReducerContext ctx, uint jobId)
    {
        if (!HasPermission(ctx, ctx.Sender, "jobs.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete jobs.");
        }
        if (ctx.Db.Job.JobId.Find(jobId) == null)
        {
            throw new Exception("Job not found.");
        }
        ctx.Db.Job.JobId.Delete(jobId);
        Log.Info($"Job {jobId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateEmployee(ReducerContext ctx, uint employeeId, string? employeeName, string? employeeSurname, string? employeePatronym, uint? jobId)
    {
        if (!HasPermission(ctx, ctx.Sender, "employees.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit employees.");
        }
        var employee = ctx.Db.Employee.EmployeeId.Find(employeeId);
        if (employee == null)
        {
            throw new Exception("Employee not found.");
        }
        if (employeeName != null)
        {
            employee.Name = employeeName;
        }
        if (employeeSurname != null)
        {
            employee.Surname = employeeSurname;
        }
        if (employeePatronym != null)
        {
            employee.Patronym = employeePatronym;
        }
        if (jobId.HasValue)
        {
            // Validate job
            if (!ctx.Db.Job.Iter().Any(j => j.JobId == jobId))
            {
                throw new Exception("Job not found.");
            }
            employee.JobId = jobId.Value;
        }
        ctx.Db.Employee.EmployeeId.Update(employee);
        Log.Info($"Employee {employeeId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteEmployee(ReducerContext ctx, uint employeeId)
    {
        if (!HasPermission(ctx, ctx.Sender, "employees.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete employees.");
        }
        if (ctx.Db.Employee.EmployeeId.Find(employeeId) == null)
        {
            throw new Exception("Employee not found");
        }
        ctx.Db.Employee.EmployeeId.Delete(employeeId);
        Log.Info($"Employee {employeeId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateTicket(ReducerContext ctx, uint ticketId, uint? routeId, uint? seatNumber, double? ticketPrice, string? paymentMethod, bool? isActive)
    {
        if (!HasPermission(ctx, ctx.Sender, "tickets.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit tickets.");
        }
        var ticket = ctx.Db.Ticket.TicketId.Find(ticketId);
        if (ticket == null)
        {
            throw new Exception("Ticket not found.");
        }
        if (routeId.HasValue)
        {   
            // Validate route
            if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
            {
                throw new Exception("Route not found");
            }
            ticket.RouteId = routeId.Value;
        }
        if (ticketPrice.HasValue)
        {
            ticket.TicketPrice = ticketPrice.Value;
        }
        if (seatNumber.HasValue)
        {
            ticket.SeatNumber = seatNumber.Value;
        }
        if (paymentMethod != null)
        {
            ticket.PaymentMethod = paymentMethod;
        }
        if (isActive.HasValue)
        {
            ticket.IsActive = isActive.Value;
        }

        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = ctx.Sender.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);
        Log.Info($"Ticket {ticketId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteTicket(ReducerContext ctx, uint ticketId)
    {
        if (!HasPermission(ctx, ctx.Sender, "tickets.delete"))
        {
           throw new Exception("Unauthorized: You do not have permission to delete tickets.");
        }
        if (ctx.Db.Ticket.TicketId.Find(ticketId) == null)
        {
            throw new Exception("Ticket not found.");
        }
        ctx.Db.Ticket.TicketId.Delete(ticketId);
        Log.Info($"Ticket {ticketId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateSale(ReducerContext ctx, uint saleId, uint? ticketId, string? ticketSoldToUser, string? ticketSoldToUserPhone, string? saleLocation, string? saleNotes)
    {
        if (!HasPermission(ctx, ctx.Sender, "sales.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit sales.");
        }
        var sale = ctx.Db.Sale.SaleId.Find(saleId);
        if (sale == null)
        {
            throw new Exception("Sale not found.");
        }

        // Update only if new value is not null
        if (ticketId.HasValue)
        {
            if (!ctx.Db.Ticket.Iter().Any(t => t.TicketId == ticketId))
            {
                throw new Exception("Ticket not found.");
            }
            sale.TicketId = ticketId.Value;
        }
        if (ticketSoldToUser != null)
        {
            sale.TicketSoldToUser = ticketSoldToUser;
        }
        if (ticketSoldToUserPhone != null)
        {
            sale.TicketSoldToUserPhone = ticketSoldToUserPhone;
        }
        if (saleLocation != null)
        {
            sale.SaleLocation = saleLocation;
        }
        if (saleNotes != null)
        {
            sale.SaleNotes = saleNotes;
        }

        ctx.Db.Sale.SaleId.Update(sale);
        Log.Info($"Sale {saleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteSale(ReducerContext ctx, uint saleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "sales.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete sales.");
        }
        // Check if the sale exists
        if (ctx.Db.Sale.SaleId.Find(saleId) == null)
        {
            throw new Exception("Sale not found.");
        }
        ctx.Db.Sale.SaleId.Delete(saleId);
        Log.Info($"Sale {saleId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRole(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit roles.");
        }
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }

        if (name != null)
        {
            role.Name = name;
        }
        if (description != null)
        {
            role.Description = description;
        }
        if (legacyRoleId.HasValue)
        {
            role.LegacyRoleId = legacyRoleId.Value;
        }
        if (priority.HasValue)
        {
            role.Priority = priority.Value;
        }
        role.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRole(ReducerContext ctx, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.delete"))
        {
            throw new Exception("Unauthorized: Missing roles.delete permission");
        }
        // Prevent deleting system roles
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        if (role.IsSystem)
        {
            throw new Exception("Cannot delete a system role");
        }
        // Remove role assignments (UserRole entries)
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.RoleId == roleId).ToList();
        foreach (var userRole in userRoles)
        {
            ctx.Db.UserRole.Id.Delete(userRole.Id); // Delete by unique ID
        }

        // Remove role permissions (RolePermission entries)
        var rolePermissions = ctx.Db.RolePermission.Iter().Where(rp => rp.RoleId == roleId).ToList();
        foreach (var rolePermission in rolePermissions)
        {
            ctx.Db.RolePermission.Id.Delete(rolePermission.Id); // Delete by unique ID
        }
        ctx.Db.Role.RoleId.Delete(roleId);
        Log.Info($"Role {roleId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdatePermission(ReducerContext ctx, uint permissionId, string? name, string? description, string? category, bool? isActive)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.edit")) // Assuming you have a permission for this
        {
            throw new Exception("Unauthorized: You do not have permission to edit permissions.");
        }

        var permission = ctx.Db.Permission.PermissionId.Find(permissionId);
        if (permission == null)
        {
            throw new Exception("Permission not found.");
        }

        // Update properties if provided (not null)
        if (name != null)
        {
            // Check for name uniqueness
            if (ctx.Db.Permission.Iter().Any(p => p.Name == name && p.PermissionId != permissionId))
            {
                throw new Exception("A permission with this name already exists.");
            }
            permission.Name = name;
        }
        if (description != null) permission.Description = description;
        if (category != null) permission.Category = category;
        if (isActive.HasValue) permission.IsActive = isActive.Value;

        ctx.Db.Permission.PermissionId.Update(permission); // Use the generated Update method.
        Log.Info($"Updated permission {permissionId}");
    }

    [SpacetimeDB.Reducer]
    public static void DeletePermission(ReducerContext ctx, uint permissionId)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.delete"))
        {
            throw new Exception("Unauthorized: Missing permissions.delete permission");
        }
        // Prevent deleting if it's still assigned to any role
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId))
        {
            throw new Exception("Cannot delete permission: it is still assigned to one or more roles.");
        }

        if (ctx.Db.Permission.PermissionId.Find(permissionId) == null)
        {
            throw new Exception("Permission not found");
        }
        ctx.Db.Permission.PermissionId.Delete(permissionId);
        Log.Info($"Permission {permissionId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void LogAdminAction(ReducerContext ctx, string userId, string action, string details, string timestamp, string ipAddress, string userAgent)
    {
        uint logId = GetNextId(ctx, "logId");
        var logEntry = new AdminActionLog
        {
            LogId = logId,
            UserId = ctx.Sender,
            Action = action,
            Details = details,
            Timestamp = (ulong)DateTimeOffset.Parse(timestamp).ToUnixTimeMilliseconds(), // Convert back to timestamp,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
        ctx.Db.admin_action_log.Insert(logEntry);
        Log.Info($"Logged Action {logEntry.Action} for user {logEntry.UserId} at: {logEntry.Timestamp}");
	}

    [SpacetimeDB.Reducer]
    public static void CreateRoleReducer(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority)
    {
        // Check if the caller has the necessary permission (e.g., "roles.create")
        if (!HasPermission(ctx, ctx.Sender, "roles.create"))
        {
            throw new Exception("Unauthorized: You do not have permission to create roles.");
        }

        // Check if a role with the same name already exists
        if (ctx.Db.Role.Iter().Any(r => r.Name == name))
        {
            throw new Exception($"A role with the name '{name}' already exists.");
        }

        uint roleId = GetNextId(ctx, "roleId");

        var role = new Role
        {
            RoleId = roleId,
            LegacyRoleId = legacyRoleId,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            Priority = priority,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            CreatedBy = ctx.Sender.ToString(),
            UpdatedBy = ctx.Sender.ToString(),
            NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
        Log.Info($"Created new role: {role.Name} ({role.RoleId})");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRoleReducer(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.edit"))
        {
            throw new Exception("Unauthorized: You do not have permission to edit roles.");
        }
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        // prevent from editing system roles
        if (role.IsSystem)
        {
            throw new Exception("Cannot modify a system role");
        }

        if (name != null)
        {   // Check if name is being changed and if it conflicts
            if (ctx.Db.Role.Iter().Any(r => r.Name == name && r.RoleId != roleId))
            {
                throw new Exception("Another role with this name already exists");
            }
            role.Name = name;
            role.NormalizedName = name.ToUpperInvariant();
        }
        if (description != null)
        {
            role.Description = description;
        }
        if (legacyRoleId.HasValue)
        {
            role.LegacyRoleId = legacyRoleId.Value;
        }
        if (priority.HasValue)
        {
            role.Priority = priority.Value;
        }
        role.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        role.UpdatedBy = ctx.Sender.ToString(); // Set the updater
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRoleReducer(ReducerContext ctx, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.delete"))
        {
            throw new Exception("Unauthorized: Missing roles.delete permission");
        }
        // Prevent deleting system roles
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        if (role.IsSystem)
        {
            throw new Exception("Cannot delete a system role");
        }
        // Remove role assignments (UserRole entries)
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.RoleId == roleId).ToList();
        foreach (var userRole in userRoles)
        {
            ctx.Db.UserRole.Id.Delete(userRole.Id); // Delete by unique ID
        }

        // Remove role permissions (RolePermission entries)
        var rolePermissions = ctx.Db.RolePermission.Iter().Where(rp => rp.RoleId == roleId).ToList();
        foreach (var rolePermission in rolePermissions)
        {
            ctx.Db.RolePermission.Id.Delete(rolePermission.Id); // Delete by unique ID
        }
        ctx.Db.Role.RoleId.Delete(roleId);
        Log.Info($"Role {roleId} has been deleted");
	}

    [SpacetimeDB.Reducer]
    public static void DebugVerifyPassword(ReducerContext ctx, string password, string storedHash)
    {
        // This reducer is for debugging purposes only
        // It allows direct testing of the VerifyPassword function
        
        bool isValid = VerifyPassword(password, storedHash);
        
        // Also compute a new hash for the same password for comparison
        string newHash = HashPassword(password);
        
        // Log the results for debugging
        Log.Info($"Debug VerifyPassword Results:");
        Log.Info($"Password: {password}");
        Log.Info($"Stored Hash: {storedHash}");
        Log.Info($"Verification Result: {isValid}");
        Log.Info($"New Hash for Same Password: {newHash}");
        Log.Info($"Would New Hash Verify: {VerifyPassword(password, newHash)}");
        
        // Compare the internal structure of the hashes (for debugging only)
        try 
        {
            var storedParts = storedHash.Split(':');
            var newParts = newHash.Split(':');
            
            if (storedParts.Length >= 2 && newParts.Length >= 2)
            {
                Log.Info($"Stored Hash Salt Length: {Convert.FromBase64String(storedParts[0]).Length}");
                Log.Info($"New Hash Salt Length: {Convert.FromBase64String(newParts[0]).Length}");
                Log.Info($"Stored Hash Value Length: {storedParts[1].Length}");
                Log.Info($"New Hash Value Length: {newParts[1].Length}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error analyzing hash structure: {ex.Message}");
        }
	}

    // ***** Authentication Tables *****
    [SpacetimeDB.Table(Public = true)]
    public partial class TotpSecret
    {
        [PrimaryKey]
        public uint Id;
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
        public string[] RedirectUris;
        public string[] AllowedScopes;
        public bool IsActive;
        public ulong CreatedAt;
        public string? CreatedBy;
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
    public static void RegisterOpenIdClient(ReducerContext ctx, string clientId, string clientSecret, string[] redirectUris, string[] allowedScopes)
    {
        var openIdConnect = new OpenIdConnect
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUris = redirectUris,
            AllowedScopes = allowedScopes,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            CreatedBy = ctx.Identity.ToString()
        };
        ctx.Db.OpenIdConnect.Insert(openIdConnect);
    }

    [SpacetimeDB.Reducer]
    public static void UpdateOpenIdClient(ReducerContext ctx, string clientId, string clientSecret, string[] redirectUris, string[] allowedScopes)
    {
        var openIdConnect = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        if (openIdConnect == null)
        {
            throw new Exception("OpenID Connect client not found");
        }
        openIdConnect.ClientSecret = clientSecret;
        openIdConnect.RedirectUris = redirectUris;
        openIdConnect.AllowedScopes = allowedScopes;
        
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
}
