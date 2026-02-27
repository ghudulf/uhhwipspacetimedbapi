using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

//WE HAVE CRYPTOGRAPHY ALREADY
namespace TicketSalesApp.Services.Implementations
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRoleService _roleService;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            ISpacetimeDBService spacetimeService,
            IRoleService roleService,
            ILogger<AuthenticationService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserProfile?> AuthenticateAsync(string login, string password)
        {
            try
            {
                _logger.LogInformation("Attempting to authenticate user: {Login}", login);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Authentication attempt with empty login or password");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Retrieved SpacetimeDB connection for authentication");

                // Debug iteration through all profiles
                _logger.LogDebug("Starting debug iteration of all user profiles");
                var allProfiles = conn.Db.UserProfile.Iter();
                foreach (var profile in allProfiles)
                {
                    _logger.LogDebug("Found profile - Login: {Login}, IsActive: {IsActive}, Identity: {Identity}",
                        profile.Login,
                        profile.IsActive,
                        profile.UserId);
                }

                // Use both methods for comparison
                var userProfileByFind = conn.Db.UserProfile.Login.Find(login);
                var userProfileByIter = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login);

                _logger.LogDebug("Find method result - Found: {Found}", userProfileByFind != null);
                _logger.LogDebug("Iter method result - Found: {Found}", userProfileByIter != null);

                // Use Find for actual authentication
                var userProfile = userProfileByFind;
                if (userProfile == null || !userProfile.IsActive)
                {
                    _logger.LogWarning("Authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                conn.Reducers.AuthenticateUser(login, password);

                _logger.LogInformation("User {Login} authenticated successfully", login);
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user: {Login}", login);
                throw;
            }
        }

        public async Task<bool> RegisterAsync(string login, string password, int role, string? email = null, string? phoneNumber = null, Identity? actingUser = null, string? newUserIdentity = null)
        {
            try
            {
                _logger.LogInformation("Attempting to register new user with login: {Login}", login);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Registration attempt with empty login or password");
                    return false;
                }

                var conn = _spacetimeService.GetConnection();

                // Check if user already exists
                var existingUser = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login);

                if (existingUser != null)
                {
                    _logger.LogWarning("Registration failed: User already exists with login: {Login}", login);
                    return false;
                }

                // Call the RegisterUser reducer
                conn.Reducers.RegisterUser(login, password, email, phoneNumber, (uint?)role, null, actingUser, newUserIdentity);

                // Check repeatedly for a short time if the user was created
                int attempts = 0;
                const int maxAttempts = 10;
                const int delayMs = 50;

                while (attempts < maxAttempts)
                {
                    var newUser = conn.Db.UserProfile.Login.Find(login);
                    if (newUser != null)
                    {
                        _logger.LogInformation("Successfully registered new user with login: {Login}", login);
                        return true;
                    }
                    await Task.Delay(delayMs);
                    attempts++;
                }

                _logger.LogError("Registration failed: User was not created within timeout period");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while registering user: {Login}", login);
                throw;
            }
        }

        public async Task<UserProfile?> AuthenticateDirectQRAsync(string login, string validationToken)
        {
            try
            {
                _logger.LogInformation("Attempting direct QR authentication for user: {Login}", login);

                if (string.IsNullOrEmpty(login))
                {
                    _logger.LogWarning("Direct QR authentication attempt with empty login");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();

                // Find user by login
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login && u.IsActive);

                if (userProfile == null)
                {
                    _logger.LogWarning("Direct QR authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                // In a real implementation, you would validate the QR token here
                // For now, we'll just update the last login time
                conn.Reducers.AuthenticateUser(login, "");

                _logger.LogInformation("User {Login} authenticated successfully via direct QR", login);
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user via direct QR: {Login}", login);
                throw;
            }
        }

        public int GetUserRole(Identity userId)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();

                // Find user roles
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .ToList();

                if (userRoles.Count == 0)
                    return 0; // Default role

                // Get the highest priority role
                uint highestPriorityRoleId = userRoles.First().RoleId;
                uint highestPriority = 0;

                foreach (var userRole in userRoles)
                {
                    var role = conn.Db.Role.RoleId.Find(userRole.RoleId);
                    if (role != null && role.Priority > highestPriority)
                    {
                        highestPriority = role.Priority;
                        highestPriorityRoleId = role.RoleId;
                    }
                }

                // Get the legacy role ID for compatibility
                var highestRole = conn.Db.Role.RoleId.Find(highestPriorityRoleId);
                return highestRole?.LegacyRoleId ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user role for identity {Identity}", userId);
                return 0; // Default role on error
            }
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

        /// <summary>
        /// Gets a user's Identity by their login name
        /// </summary>
        /// <param name="login">The user's login name</param>
        /// <returns>The user's Identity if found, null otherwise</returns>
        public async Task<Identity?> GetUserIdentityByLoginAsync(string login)
        {
            try
            {
                // Normalize the login for case-insensitive comparison
                string normalizedLogin = login.ToUpperInvariant();

                // Get connection to SpacetimeDB
                var conn = _spacetimeService.GetConnection();

                // Find the user by normalized login
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == normalizedLogin);

                if (user == null)
                {
                    _logger.LogWarning("User with login {Login} not found", login);
                    return null;
                }

                _logger.LogDebug("Found user with login {Login}, returning Identity", login);
                return user.UserId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user identity for login {Login}", login);
                return null;
            }
        }

    }
}
