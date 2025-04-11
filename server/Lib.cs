using System;
using System.Text;
using SpacetimeDB;

public static partial class Module
{
	// ---------- Table Definitions ----------
	[SpacetimeDB.Table(Public = true)]
    public partial class Incident
	{
		[PrimaryKey]
        public uint IncidentId;         // Auto-incremented
        public ulong IncidentTime;      // When the incident occurred
        public uint? BusId;             // References Bus.BusId
        public uint? RouteId;           // References Route.RouteId
        public uint? EmployeeId;        // References Employee.EmployeeId
        public string IncidentType;     // "Accident", "Breakdown", "Passenger Incident", "Traffic", "Weather"
        public string Description;      // Description of the incident
        public string? Severity;        // "Low", "Medium", "High", "Critical"
        public string? Status;          // "Reported", "Under Investigation", "Resolved", "Closed"
        public string? Resolution;      // How the incident was resolved
        public ulong? ResolutionTime;   // When the incident was resolved
        public string? Location;        // Where the incident occurred
        public Identity? ReportedBy;    // Who reported the incident
        public string[]? Witnesses;     // Witnesses to the incident
        public string[]? Attachments;   // URLs to attachments (photos, documents)
        public bool? RequiresFollowUp;  // Whether the incident requires follow-up
        public string? FollowUpNotes;   // Notes for follow-up
        public ulong? FollowUpDate;     // When follow-up is scheduled
    }

    



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


    [SpacetimeDB.Table]
    public partial class DiscountIdCounter
	{
		[PrimaryKey]
        public string Key = "discountId";
        public uint NextId = 0;
	}

    [SpacetimeDB.Table]
    public partial class SeatConfigurationIdCounter
	{
		[PrimaryKey]
        public string Key = "seatConfigurationId";
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

    [SpacetimeDB.Table]
    public partial class WebAuthnCredentialIdCounter
    {
        [PrimaryKey]
        public string Key = "webAuthnCredentialId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class WebAuthnChallengeIdCounter
    {
        [PrimaryKey]
        public string Key = "webAuthnChallengeId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class TwoFactorTokenIdCounter
    {
        [PrimaryKey]
        public string Key = "twoFactorTokenId";
        public uint NextId = 0;
    }

    [SpacetimeDB.Table]
    public partial class TotpSecretIdCounter
    {
        [PrimaryKey]
        public string Key = "totpSecretId";
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

        Log.Debug($"PBKDF2: Beginning iteration process (1 to {iterations - 1})");
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
            Log.Debug($"ComputeSha256: Processing chunk {i / 64 + 1} of {chunkCount}");
            
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

    /// <summary>
    /// Registers a new user in the system.
    /// </summary>
    /// <param name="ctx">The reducer context providing access to the database and sender identity.</param>
    /// <param name="login">The username for the new user account. Must be unique.</param>
    /// <param name="password">The password for the new user account. Will be hashed before storage.</param>
    /// <param name="email">The email address for the new user. Used for communications and recovery.</param>
    /// <param name="phoneNumber">The phone number for the new user. Used for communications and 2FA.</param>
    /// <param name="roleId">Optional. The specific role ID to assign to the user. Takes precedence over roleName if both are provided.</param>
    /// <param name="roleName">Optional. The name of the role to assign to the user. Used only if roleId is not provided.</param>
    /// <param name="actingUser">Optional. The identity of the user performing this action. Used for permission checks.</param>
    /// <param name="newUserIdentity">Optional. A pre-generated identity string from the /v1/identity endpoint. This is the hex-encoded string representation of the identity.
    /// This allows creating users with specific identities rather than using the sender's identity.
    /// Can be generated using:
    /// POST /v1/identity HTTP endpoint which returns:
    /// {
    ///     "identity": string,//its a string of hex characters
    ///     "token": string
    /// }
    /// The identity string can then be passed to this reducer to create a user with that identity.
    /// </param>
    /// <remarks>
    /// Required Permissions: 
    /// - None for self-registration
    /// - "user:create" for creating other users
    /// - "role:assign" for assigning roles other than the default "User" role
    /// 
    /// If no role is specified, the default "User" role will be assigned.
    /// </remarks>
    [SpacetimeDB.Reducer]
    public static void RegisterUser(ReducerContext ctx, string login, string password, string email, string phoneNumber, uint? roleId = null, string? roleName = null, Identity? actingUser = null, string? newUserIdentity = null)
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

        // Determine which identity to use - either the provided newUserIdentity (converted to Identity type),
        // or fall back to the sender's identity
        Identity userIdentity = ctx.Sender;
        if (!string.IsNullOrEmpty(newUserIdentity))
        {
            // Convert the string identity to the Identity type.
            // The string should be in hex format, e.g., "0x123456789abcdef0"
            try
            {
                userIdentity = Identity.FromHexString(newUserIdentity);
                Log.Info($"Using provided identity: {userIdentity} for new user {login}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse provided identity: {ex.Message}");
                throw new Exception("Invalid identity format provided. Identity should be in hex format, e.g., '0x123456789abcdef0'.");
            }
        }

        // Create the new user
        var user = new UserProfile
        {
            UserId = userIdentity, // Use either the provided identity or the sender's identity
            LegacyUserId = userId,
            Login = login,
            PasswordHash = hashedPassword,
            Email = email,
            PhoneNumber = phoneNumber,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            LastLoginAt = null,
            LegacyGuid = Guid.NewGuid().ToString(),
            EmailConfirmed = true // email is optional so this is just gonna be defaulted to true
        };
        ctx.Db.UserProfile.Insert(user);
        Log.Info($"User {login} registered successfully with Identity {user.UserId}");
        //NO FUCKING NEED TO CREATE USER SETTINGS - THATS A USER CAllABLE REDUCER THAT SPACETIMEDB WONT PERMIT CALLING FROM HERE IF YOU WANT TO DO IT - MAKE A SEPARATE METHOD
        Log.Info($"User {login} registered successfully");

        // Assign role if specified by roleId
        if (roleId.HasValue)
        {
            // Validate that the role exists
            if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId.Value))
            {
                throw new Exception($"Role with ID {roleId.Value} not found.");
            }

            AssignRole(ctx, user.UserId, roleId.Value, actingUser);
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

            AssignRole(ctx, user.UserId, role.RoleId, actingUser);
            Log.Info($"Assigned role '{roleName}' to user {login}");
        }
        else
        {
            // Assign default "User" role if no role specified
            var userRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
            if (userRole != null)
            {
                AssignRole(ctx, user.UserId, userRole.RoleId, actingUser);
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
    public static void CreateMaintenance(ReducerContext ctx, uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Authorization check - verify the effective user has the required permission
        if (!HasPermission(ctx, effectiveUser, "schedule_maintenance")) // CreateMaintenance PERM CHECK
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
    public static void ClaimUserAccount(ReducerContext ctx, string login, string password, string? newUserIdentity = null)
    {
        // Determine which identity to use - either the provided newUserIdentity (converted to Identity type),
        // or fall back to the sender's identity
        Identity userIdentity = ctx.Sender;
        if (!string.IsNullOrEmpty(newUserIdentity))
        {
            // Convert the string identity to the Identity type.
            // The string should be in hex format, e.g., "0x123456789abcdef0"
            try
            {
                userIdentity = Identity.FromHexString(newUserIdentity);
                Log.Info($"Using provided identity: {userIdentity} for new user {login}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse provided identity: {ex.Message}");
                throw new Exception("Invalid identity format provided. Identity should be in hex format, e.g., '0x123456789abcdef0'.");
            }
        }
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
        
        if (user.IsActive && user.UserId != userIdentity)
        {
            Log.Error($"Account {login} is already claimed by another identity");
            return;
        }
        
        // Update the user with the caller's identity
        user.UserId = userIdentity;
        user.IsActive = true;
        user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.Login.Update(user);

        // Update user roles to reference the new identity
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.UserId == user.UserId).ToList();
        foreach (var role in userRoles)
        {
            role.UserId = userIdentity;
            ctx.Db.UserRole.Id.Update(role);
        }

        Log.Info($"User {login} successfully claimed by identity {userIdentity}");
    }

    

    // ---------- Reducers ----------
    
    

    [SpacetimeDB.Reducer]
    public static void UpdateUser(ReducerContext ctx, Identity userId, string? login, string? passwordHash,
        int? role, string? phoneNumber, string? email, bool? isActive, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "users.edit")) // UpdateUser PERM CHECK
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
                    AssignRole(ctx, userId, roleEntity.RoleId, actingUser);
                }
            }
        }
    }

    [SpacetimeDB.Reducer]
    public static void ActivateUser(ReducerContext ctx, Identity userId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "users.edit")) // ActivateUser PERM CHECK
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
        Log.Info($"User {userId} activated by {effectiveUser}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateUser(ReducerContext ctx, Identity userId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "users.edit")) // DeactivateUser PERM CHECK
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
        Log.Info($"User {userId} deactivated by {effectiveUser}");
    }
    
    [SpacetimeDB.Reducer]
    public static void ChangePassword(ReducerContext ctx, Identity userId, string currentPassword, string newPassword, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
        {
            throw new Exception("User not found.");
        }
        
        // Check if this is the user changing their own password or an admin
        bool isAdmin = HasPermission(ctx, effectiveUser, "users.edit"); // ChangePassword PERM CHECK
        bool isSelf = effectiveUser.Equals(userId);
        
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
    public static void DeleteUser(ReducerContext ctx, Identity userId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "users.delete")) // DeleteUser PERM CHECK
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
    public static void UpdateRoute(ReducerContext ctx, uint routeId, string? startPoint, string? endPoint, uint? driverId, uint? busId, string? travelTime, bool? isActive, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "routes.edit")) // UpdateRoute PERM CHECK
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
    public static void DeleteRoute(ReducerContext ctx, uint routeId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "routes.delete")) // DeleteRoute PERM CHECK
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
    public static void ActivateRoute(ReducerContext ctx, uint routeId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "routes.edit")) // ActivateRoute PERM CHECK
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
        Log.Info($"Route {routeId} activated by {effectiveUser}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateRoute(ReducerContext ctx, uint routeId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "routes.edit")) // DeactivateRoute PERM CHECK
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
        Log.Info($"Route {routeId} deactivated by {effectiveUser}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRouteSchedule(ReducerContext ctx, uint scheduleId, uint? routeId, string? startPoint,
        string? endPoint, string[]? routeStops, ulong? departureTime, ulong? arrivalTime,
        double? price, uint? availableSeats, string[]? daysOfWeek, string[]? busTypes, uint? stopDurationMinutes,
        bool? isRecurring, string[]? estimatedStopTimes,
        double[]? stopDistances, string? notes, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "schedules.edit")) // UpdateRouteSchedule PERM CHECK
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
        schedule.UpdatedBy = effectiveUser.ToString(); // Use the effective user identity

        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Route schedule {scheduleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRouteSchedule(ReducerContext ctx, uint scheduleId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "schedules.delete")) // DeleteRouteSchedule PERM CHECK
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
    public static void ActivateSchedule(ReducerContext ctx, uint scheduleId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "schedules.edit")) // ActivateSchedule PERM CHECK
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
        schedule.UpdatedBy = effectiveUser.ToString();
        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Schedule {scheduleId} activated by {effectiveUser}");
    }
    
    [SpacetimeDB.Reducer]
    public static void DeactivateSchedule(ReducerContext ctx, uint scheduleId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Check for the required permission
        if (!HasPermission(ctx, effectiveUser, "schedules.edit")) // DeactivateSchedule PERM CHECK
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
        schedule.UpdatedBy = effectiveUser.ToString();
        ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
        Log.Info($"Schedule {scheduleId} deactivated by {effectiveUser}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateJob(ReducerContext ctx, uint jobId, string? jobTitle, string? internship, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "jobs.edit")) // UpdateJob PERM CHECK
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
    public static void DeleteJob(ReducerContext ctx, uint jobId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "jobs.delete")) // DeleteJob PERM CHECK
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
    public static void UpdateEmployee(ReducerContext ctx, uint employeeId, string? employeeName, string? employeeSurname, string? employeePatronym, uint? jobId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "employees.edit")) // UpdateEmployee PERM CHECK
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
    public static void DeleteEmployee(ReducerContext ctx, uint employeeId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "employees.delete")) // DeleteEmployee PERM CHECK
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
    public static void LogAdminAction(ReducerContext ctx, string userId, string action, string details, string timestamp, string ipAddress, string userAgent, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        uint logId = GetNextId(ctx, "logId");
        var logEntry = new AdminActionLog
        {
            LogId = logId,
            UserId = effectiveUser,
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

    



}
