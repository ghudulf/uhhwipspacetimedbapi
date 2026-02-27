// Importing necessary namespaces for the service
using SpacetimeDB; // Import SpacetimeDB library
using SpacetimeDB.Types; // Import SpacetimeDB types
using Microsoft.Extensions.Configuration; // Import configuration extensions
using Microsoft.Extensions.Logging; // Import logging extensions
using System; // Import system namespace
using System.Threading.Tasks; // Import task-based asynchronous pattern
using TicketSalesApp.Services.Interfaces;
using System.Collections.Concurrent; // Import service interfaces
using System.Collections.Generic; // Import generic collections

// Define the namespace for the service implementation
namespace TicketSalesApp.Services.Implementations
{
    // Define the SpacetimeDBService class implementing ISpacetimeDBService interface
    public class SpacetimeDBService : ISpacetimeDBService
    {
        // Private fields for configuration, logger, and connection management
        private readonly IConfiguration _configuration; // Configuration for the service
        private readonly ILogger<SpacetimeDBService> _logger; // Logger for the service
        private DbConnection? _connection; // Database connection object
        private Identity? _localIdentity; // Local identity for the connection
        private readonly ConcurrentQueue<(string Command, Dictionary<string, object> Args)> _inputQueue; // Thread-safe queue for commands
        private volatile bool _isConnecting = false; // Flag to track connection state
        private volatile bool _subscriptionApplied = false; // Flag to track if subscription has been applied
        private readonly bool _logReducerLogsToFile; // Flag to control whether reducer logs are logged to file

        // Constructor to initialize configuration and logger
        public SpacetimeDBService(IConfiguration configuration, ILogger<SpacetimeDBService> logger)
        {
            _configuration = configuration; // Assign configuration
            _logger = logger; // Assign logger
            _inputQueue = new ConcurrentQueue<(string Command, Dictionary<string, object> Args)>(); // Initialize the input queue
            _logReducerLogsToFile = _configuration.GetValue<bool>("SpacetimeDB:LogReducerLogsToFile", true); // Default to true for backward compatibility
            _logger.LogInformation("SpacetimeDBService initialized");
        }

        // Method to establish a connection to the database
        public DbConnection Connect()
        {
            // Prevent multiple connection attempts
            if (_isConnecting)
            {
                _logger.LogDebug("Connection attempt already in progress, skipping duplicate request");
                return _connection ?? throw new InvalidOperationException("Connection is being established");
            }

            // If we already have an established connection, return it
            if (_connection != null && _connection.IsActive)
            {
                _logger.LogDebug("Reusing existing active connection");
                return _connection;
            }

            try
            {
                _isConnecting = true;

                // Retrieve host and database name from configuration
                var host = _configuration["SpacetimeDB:Host"] ?? "http://localhost:3000"; // Default host
                var databaseName = _configuration["SpacetimeDB:DatabaseName"] ?? "avtopark"; // Default database name

                // Log the connection attempt
                _logger.LogInformation("Connecting to SpacetimeDB at {Host} database {Database}", host, databaseName);

                // Initialize authentication token storage
                AuthToken.Init(".spacetime_csharp_avtopark");
                _logger.LogDebug("Authentication token initialized");

                // Build the database connection with necessary callbacks
                _logger.LogDebug("Building database connection with callbacks");
                _connection = DbConnection.Builder()
                        .WithUri(host) // Set the URI for the connection
                        .WithDatabaseName(databaseName) // Set the database name
                        .WithToken(AuthToken.Token) // Set the authentication token
                        .WithConfirmedReads(true) // Enable confirmed reads for durability (default: true). Set to false for low-latency scenarios where eventual consistency is acceptable.
                        .OnConnect(OnConnected) // Set the on-connect callback
                        .OnConnectError(OnConnectError) // Set the on-connect-error callback
                        .OnDisconnect(OnDisconnected) // Set the on-disconnect callback
                        .Build(); // Build the connection

                _logger.LogInformation("Database connection built successfully");
                return _connection; // Return the established connection
            }
            catch (Exception ex)
            {
                // Log any errors that occur during connection
                _logger.LogError(ex, "Error connecting to SpacetimeDB: {ErrorMessage}", ex.Message);
                _isConnecting = false;
                throw; // Rethrow the exception
            }
        }

        // Method to get the current database connection
        public DbConnection GetConnection()
        {
            if (_connection == null)
            {
                // Throw an exception if the connection is not initialized
                _logger.LogError("Attempted to get connection before initialization");
                throw new InvalidOperationException("SpacetimeDB connection not initialized or not yet established. Call Connect() first and wait for connection to complete.");
            }
            _logger.LogDebug("Retrieved existing database connection");
            return _connection; // Return the current connection
        }

        // Method to get database identity from SpacetimeDB API
        private async Task<string?> GetDatabaseIdentityAsync(string databaseNameOrIdentity, string token)
        {
            try
            {
                var host = _configuration["SpacetimeDB:Host"] ?? "http://localhost:3000";
                var baseUrl = host.TrimEnd('/');
                
                // Try the /identity endpoint first (simpler, returns just the hex string)
                var identityUrl = $"{baseUrl}/v1/database/{databaseNameOrIdentity}/identity";
                
                _logger.LogDebug("Fetching database identity from: {Url}", identityUrl);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.GetAsync(identityUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var identity = await response.Content.ReadAsStringAsync();
                    identity = identity.Trim().Trim('"'); // Remove quotes if present
                    
                    _logger.LogDebug("Successfully obtained database identity: {Identity}", identity);
                    return identity;
                }
                
                // If /identity endpoint fails, try the describe endpoint
                _logger.LogDebug("Identity endpoint failed with {Status}, trying describe endpoint", response.StatusCode);
                
                var describeUrl = $"{baseUrl}/v1/database/{databaseNameOrIdentity}";
                var describeResponse = await httpClient.GetAsync(describeUrl);
                
                if (describeResponse.IsSuccessStatusCode)
                {
                    var describeContent = await describeResponse.Content.ReadAsStringAsync();
                    var describeJson = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(describeContent);
                    
                    if (describeJson.TryGetProperty("database_identity", out var identityElement))
                    {
                        var identity = identityElement.GetString();
                        _logger.LogDebug("Successfully obtained database identity from describe: {Identity}", identity);
                        return identity;
                    }
                }
                
                _logger.LogWarning("Failed to get database identity. Status: {Status}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting database identity: {Message}", ex.Message);
                return null;
            }
        }

        // Method to get a short-lived token with proper permissions for HTTP API calls
        private async Task<string?> GetWebSocketTokenAsync()
        {
            try
            {
                var host = _configuration["SpacetimeDB:Host"] ?? "http://localhost:3000";
                var currentToken = AuthToken.Token;

                if (string.IsNullOrEmpty(currentToken))
                {
                    _logger.LogWarning("Cannot get WebSocket token: No current token available");
                    return null;
                }

                var baseUrl = host.TrimEnd('/');
                var url = $"{baseUrl}/v1/identity/websocket-token";

                _logger.LogDebug("Requesting WebSocket token from: {Url}", url);

                using var httpClient = new HttpClient();
                // Use Basic authorization with the current Spacetime token
                var authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"token:{currentToken}"));
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authValue}");
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.PostAsync(url, null);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed to get WebSocket token. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseContent);
                
                if (tokenResponse.TryGetProperty("token", out var tokenElement))
                {
                    var newToken = tokenElement.GetString();
                    _logger.LogDebug("Successfully obtained WebSocket token");
                    return newToken;
                }

                _logger.LogWarning("WebSocket token response did not contain 'token' field");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting WebSocket token: {Message}", ex.Message);
                return null;
            }
        }

        // Method to fetch logs from SpacetimeDB HTTP API
        public async Task<string> FetchLogsAsync(int numLines = 100, bool follow = false)
        {
            try
            {
                var host = _configuration["SpacetimeDB:Host"] ?? "http://localhost:3000";
                var databaseName = _configuration["SpacetimeDB:DatabaseName"] ?? "avtopark";
                
                // Try multiple token sources in order of preference:
                // 1. Configuration AdminToken (highest priority - owner token)
                // 2. Environment variable SPACETIME_TOKEN
                // 3. WebSocket token (short-lived token from current connection)
                // 4. AuthToken (client connection token - may not have log permissions)
                var token = _configuration["SpacetimeDB:AdminToken"]
                           ?? Environment.GetEnvironmentVariable("SPACETIME_TOKEN")
                           ?? await GetWebSocketTokenAsync()
                           ?? AuthToken.Token;

                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Cannot fetch logs: No authentication token available");
                    return "Warning: No authentication token available. Logs cannot be fetched.";
                }

                // Try to get database identity dynamically, fall back to configured value or name
                string? databaseIdentity = null;
                
                // First try configured identity
                databaseIdentity = _configuration["SpacetimeDB:DatabaseIdentity"];
                
                // If not configured, try to fetch it from the API using database name
                if (string.IsNullOrEmpty(databaseIdentity))
                {
                    _logger.LogDebug("Database identity not configured, fetching from API using name: {Name}", databaseName);
                    databaseIdentity = await GetDatabaseIdentityAsync(databaseName, token);
                }
                
                var baseUrl = host.TrimEnd('/');
                
                // Try with identity first if available
                if (!string.IsNullOrEmpty(databaseIdentity))
                {
                    var identityUrl = $"{baseUrl}/v1/database/{databaseIdentity}/logs?num_lines={numLines}";
                    if (follow) identityUrl += "&follow=true";
                    
                    _logger.LogDebug("Attempting to fetch logs using database identity: {Identity}", databaseIdentity);
                    
                    using var httpClient1 = new HttpClient();
                    httpClient1.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    httpClient1.Timeout = TimeSpan.FromSeconds(30);
                    
                    var response1 = await httpClient1.GetAsync(identityUrl);
                    
                    if (response1.IsSuccessStatusCode)
                    {
                        var logs = await response1.Content.ReadAsStringAsync();
                        _logger.LogInformation("Successfully fetched {Lines} lines of logs using identity", numLines);
                        return logs;
                    }
                    else if (response1.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning("Current identity does not have permission to view module logs.");
                        return GetForbiddenMessage(databaseName);
                    }
                    else
                    {
                        _logger.LogDebug("Identity-based fetch failed with {Status}, trying with database name", response1.StatusCode);
                    }
                }
                
                // Fall back to database name
                var nameUrl = $"{baseUrl}/v1/database/{databaseName}/logs?num_lines={numLines}";
                if (follow) nameUrl += "&follow=true";
                
                _logger.LogDebug("Fetching logs using database name: {Name}", databaseName);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(nameUrl);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("Current identity does not have permission to view module logs.");
                    return GetForbiddenMessage(databaseName);
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed to fetch logs. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    _logger.LogInformation("Reducer logs not available via HTTP API. Check SpacetimeDB server console for reducer logs.");
                    return $"Warning: Failed to fetch logs: {response.StatusCode} - {errorContent}";
                }

                var finalLogs = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully fetched {Lines} lines of logs using database name", numLines);
                
                return finalLogs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching SpacetimeDB logs: {Message}", ex.Message);
                return $"Warning: Error fetching logs: {ex.Message}";
            }
        }

        private string GetForbiddenMessage(string databaseName)
        {
            _logger.LogInformation("To enable log fetching:");
            _logger.LogInformation("  Option 1: Add admin token to appsettings.json:");
            _logger.LogInformation("    \"SpacetimeDB\": {{");
            _logger.LogInformation("      \"AdminToken\": \"<your-token-from-spacetime-login-show>\"");
            _logger.LogInformation("    }}");
            _logger.LogInformation("  Option 2: Set SPACETIME_TOKEN environment variable");
            _logger.LogInformation("  Option 3: Use CLI: spacetime logs {DatabaseName}", databaseName);
            return $"Warning: Current identity does not have permission to view module logs.\n" +
                   $"To enable log fetching, add AdminToken to appsettings.json or set SPACETIME_TOKEN env var.\n" +
                   $"Run 'spacetime login show --token' to get your token.\n\n" +
                   $"Alternatively, use the SpacetimeDB CLI:\n" +
                   $"  spacetime logs {databaseName}";
        }

        // Method to fetch and log recent reducer logs (convenience method)
        public async Task<string> FetchReducerLogsAsync(string reducerName = "RegisterOpenIdClient", int numLines = 200)
        {
            try
            {
                _logger.LogDebug("Attempting to fetch recent logs for reducer: {ReducerName}", reducerName);
                
                var allLogs = await FetchLogsAsync(numLines, follow: false);
                
                if (allLogs.StartsWith("Warning:"))
                {
                    _logger.LogInformation("Reducer logs not available via HTTP API. Check SpacetimeDB server console for reducer logs.");
                    return allLogs;
                }

                // Filter logs for the specific reducer
                var lines = allLogs.Split('\n');
                var reducerLogs = lines.Where(line => line.Contains($"[{reducerName}]")).ToList();

                if (reducerLogs.Count == 0)
                {
                    _logger.LogDebug("No logs found for reducer: {ReducerName} in fetched logs", reducerName);
                    return $"No logs found for reducer: {reducerName} in the last {numLines} log lines.\n" +
                           $"Check the SpacetimeDB server console for detailed reducer logs.";
                }

                var filteredLogs = string.Join("\n", reducerLogs);
                _logger.LogInformation("Found {Count} log lines for reducer: {ReducerName}", reducerLogs.Count, reducerName);
                
                // Log to console - Serilog filter will handle whether it goes to file
                _logger.LogInformation("=== SpacetimeDB Reducer Logs: {ReducerName} ===", reducerName);
                foreach (var line in reducerLogs)
                {
                    _logger.LogInformation(line);
                }
                _logger.LogInformation("=== End of Reducer Logs ===");

                return filteredLogs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching reducer logs: {Message}", ex.Message);
                return $"Warning: Error fetching reducer logs: {ex.Message}\n" +
                       $"Check the SpacetimeDB server console for detailed reducer logs.";
            }
        }

        // Method to get the local identity
        public Identity? GetLocalIdentity()
        {
            _logger.LogDebug("Retrieved local identity: {Identity}", _localIdentity);
            return _localIdentity; // Return the local identity
        }

        // Method to check if the connection is active
        public bool IsConnected()
        {
            return _connection != null && _connection.IsActive;
        }

        // Method to check if the subscription has been applied
        public bool IsSubscriptionReady()
        {
            return _subscriptionApplied;
        }

        // Method to disconnect from the database
        public void Disconnect()
        {
            _logger.LogInformation("Disconnecting from SpacetimeDB...");

            if (_connection != null)
            {
                // Log the disconnection attempt
                _logger.LogInformation("Disconnecting from SpacetimeDB connection...");
                _connection.Disconnect(); // Disconnect the connection
                _connection = null; // Reset the connection
                _localIdentity = null; // Reset the local identity
                _logger.LogInformation("SpacetimeDB disconnected successfully");
            }
            else
            {
                _logger.LogWarning("Disconnect called but no active connection exists");
            }
        }

        // Method to enqueue a command for processing
        public void EnqueueCommand(string command, Dictionary<string, object> args)
        {
            _inputQueue.Enqueue((command, args)); // Add the command to the queue
            _logger.LogDebug("Command enqueued: {Command} with {ArgCount} arguments", command, args.Count);
        }

        // Method to process a single frame tick and any pending commands
        public void ProcessFrameTick()
        {
            if (_connection == null)
            {
                _logger.LogDebug("Skipping frame tick - connection not fully established");
                return;
            }

            try
            {
                _logger.LogTrace("Processing frame tick");
                _connection.FrameTick();
                ProcessCommands();
            }
            catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
            {
                // This can happen when multiple reducers are called in rapid succession
                // and the client cache hasn't been fully updated yet. This is not a critical error.
                _logger.LogWarning("Duplicate key detected during frame tick processing. This is expected when multiple reducers are called rapidly. Key: {Message}", ex.Message);
                // Continue processing - the data is already in the cache
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing frame tick: {ErrorMessage}", ex.Message);
                // Don't rethrow as this would break the application flow
            }
        }

        // Method to subscribe to all tables (excludes event tables in SpacetimeDB 2.0)
        public void SubscribeToAllTables()
        {
            if (_connection == null)
            {
                _logger.LogError("Attempted to subscribe to all tables without an active connection");
                throw new InvalidOperationException("SpacetimeDB connection not initialized or not yet established. Call Connect() first and wait for connection to complete.");
            }

            _logger.LogInformation("Subscribing to all regular tables (event tables excluded)");
            _connection.SubscriptionBuilder()
                .OnApplied(OnSubscriptionApplied)
                .OnError(OnSubscriptionError)
                .SubscribeToAllTables();

            _logger.LogInformation("Subscribed to all regular tables successfully");
        }

        // Method to subscribe to event tables explicitly
        public SubscriptionHandle SubscribeToEventTables()
        {
            if (_connection == null)
            {
                _logger.LogError("Attempted to subscribe to event tables without an active connection");
                throw new InvalidOperationException("SpacetimeDB connection not initialized or not yet established. Call Connect() first and wait for connection to complete.");
            }

            _logger.LogInformation("Subscribing to event tables");
            var subscriptionHandle = _connection.SubscriptionBuilder()
                .OnApplied(ctx => _logger.LogInformation("Event table subscription applied successfully"))
                .OnError((ctx, err) => _logger.LogError(err, "Error subscribing to event tables: {ErrorMessage}", err.Message))
                .Subscribe(new[] {
                    "SELECT * FROM AuthenticationEvent",
                    "SELECT * FROM TicketSaleEvent",
                    "SELECT * FROM BusStatusEvent",
                    "SELECT * FROM RouteScheduleEvent",
                    "SELECT * FROM MaintenanceEvent"
                });

            _logger.LogInformation("Subscribed to event tables successfully");
            return subscriptionHandle;
        }

        // Method to subscribe to specific queries
        public SubscriptionHandle SubscribeToQueries(string[] queries)
        {
            if (_connection == null)
            {
                _logger.LogError("Attempted to subscribe to queries without an active connection");
                throw new InvalidOperationException("SpacetimeDB connection not initialized or not yet established. Call Connect() first and wait for connection to complete.");
            }

            _logger.LogInformation("Subscribing to {Count} queries", queries.Length);
            foreach (var query in queries)
            {
                _logger.LogDebug("Query: {Query}", query);
            }

            var subscriptionHandle = _connection.SubscriptionBuilder()
                .OnApplied(OnSubscriptionApplied)
                .OnError(OnSubscriptionError)
                .Subscribe(queries);

            _logger.LogInformation("Subscribed to {Count} queries successfully", queries.Length);
            return subscriptionHandle;
        }

        // Private method to process commands from the queue
        private void ProcessCommands()
        {
            if (_connection == null)
            {
                _logger.LogDebug("ProcessCommands called but connection not fully established");
                return;
            }

            int processedCount = 0;
            while (_inputQueue.TryDequeue(out var command))
            {
                try
                {
                    _logger.LogDebug("Processing command: {Command}", command.Command);
                    ProcessCommand(command.Command, command.Args);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing command: {Command}, Error: {ErrorMessage}", command.Command, ex.Message);
                }
            }

            if (processedCount > 0)
            {
                _logger.LogDebug("Processed {Count} commands from queue", processedCount);
            }
        }

        // Private method to process a single command
        private void ProcessCommand(string command, Dictionary<string, object> args)
        {
            if (_connection == null)
            {
                _logger.LogWarning("Cannot process command {Command}: connection not fully established.", command);
                return;
            }

            var reducers = _connection.Reducers;
            _logger.LogDebug("Processing command: {Command} with {ArgCount} arguments", command, args.Count);

            switch (command.ToLowerInvariant())
            {
                // User Management
                case "registeruser":
                    if (TryGetValue<string>(args, "login", out var login) &&
                        TryGetValue<string>(args, "password", out var password) &&
                        TryGetValue<string>(args, "email", out var email) &&
                        TryGetValue<string>(args, "phoneNumber", out var phoneNumber))
                    {
                        uint? roleId = null;
                        string? roleName = null;

                        if (args.ContainsKey("roleId"))
                            roleId = Convert.ToUInt32(args["roleId"]);

                        if (args.ContainsKey("roleName"))
                            roleName = args["roleName"].ToString();

                        _logger.LogInformation("Processing RegisterUser command for user: {Login}", login);
                        reducers.RegisterUser(login, password, email, phoneNumber, roleId, roleName,null, null);
                        _logger.LogInformation("RegisterUser command completed for user: {Login}", login);
                    }
                    else
                    {
                        _logger.LogWarning("RegisterUser command missing required parameters");
                    }
                    break;

                case "authenticateuser":
                    if (TryGetValue<string>(args, "login", out var authLogin) &&
                        TryGetValue<string>(args, "password", out var authPassword))
                    {
                        _logger.LogInformation("Processing AuthenticateUser command for user: {Login}", authLogin);
                        reducers.AuthenticateUser(authLogin, authPassword);
                        _logger.LogInformation("AuthenticateUser command completed for user: {Login}", authLogin);
                    }
                    else
                    {
                        _logger.LogWarning("AuthenticateUser command missing required parameters");
                    }
                    break;

                case "createqrsession":
                    if (TryGetValue<string>(args, "sessionId", out var sessionId) &&
                        TryGetValue<Identity>(args, "userId", out var userId) &&
                        TryGetValue<string>(args, "validationCode", out var validationCode) &&
                        TryGetValue<ulong>(args, "expiryTime", out var expiryTime) &&
                        TryGetValue<string>(args, "initiatingDevice", out var initiatingDevice))
                    {
                        _logger.LogInformation("Processing CreateQRSession command for user: {UserId}", userId);
                        reducers.CreateQrSession(sessionId, userId, validationCode, expiryTime, initiatingDevice);
                        _logger.LogInformation("CreateQRSession command completed for session: {SessionId}", sessionId);
                    }
                    else
                    {
                        _logger.LogWarning("CreateQRSession command missing required parameters");
                    }
                    break;

                case "validateqrcode":
                    if (TryGetValue<string>(args, "sessionId", out var validateSessionId) &&
                        TryGetValue<string>(args, "validationCode", out var validateCode))
                    {
                        _logger.LogInformation("Processing ValidateQRCode command for session: {SessionId}", validateSessionId);
                        reducers.ValidateQrCode(validateSessionId, validateCode);
                        _logger.LogInformation("ValidateQRCode command completed for session: {SessionId}", validateSessionId);
                    }
                    else
                    {
                        _logger.LogWarning("ValidateQRCode command missing required parameters");
                    }
                    break;

                case "useqrsession":
                    if (TryGetValue<string>(args, "sessionId", out var useSessionId))
                    {
                        _logger.LogInformation("Processing UseQRSession command for session: {SessionId}", useSessionId);
                        reducers.UseQrSession(useSessionId);
                        _logger.LogInformation("UseQRSession command completed for session: {SessionId}", useSessionId);
                    }
                    else
                    {
                        _logger.LogWarning("UseQRSession command missing required parameters");
                    }
                    break;

                case "deleteqrsession":
                    if (TryGetValue<string>(args, "sessionId", out var deleteSessionId))
                    {
                        _logger.LogInformation("Processing DeleteQRSession command for session: {SessionId}", deleteSessionId);
                        reducers.DeleteQrSession(deleteSessionId);
                        _logger.LogInformation("DeleteQRSession command completed for session: {SessionId}", deleteSessionId);
                    }
                    else
                    {
                        _logger.LogWarning("DeleteQRSession command missing required parameters");
                    }
                    break;

                case "assignrole":
                    if (TryGetValue<Identity>(args, "userId", out var assignUserId) &&
                        TryGetValue<uint>(args, "roleId", out var assignRoleId))
                    {
                        _logger.LogInformation("Processing AssignRole command for user: {UserId}, role: {RoleId}", assignUserId, assignRoleId);
                        reducers.AssignRole(assignUserId, assignRoleId, null);
                        _logger.LogInformation("AssignRole command completed for user: {UserId}", assignUserId);
                    }
                    else
                    {
                        _logger.LogWarning("AssignRole command missing required parameters");
                    }
                    break;

                case "grantpermissiontorole":
                    if (TryGetValue<uint>(args, "roleId", out var grantRoleId) &&
                        TryGetValue<uint>(args, "permissionId", out var permissionId))
                    {
                        _logger.LogInformation("Processing GrantPermissionToRole command for role: {RoleId}, permission: {PermissionId}", grantRoleId, permissionId);
                        reducers.GrantPermissionToRole(grantRoleId, permissionId, null);
                        _logger.LogInformation("GrantPermissionToRole command completed for role: {RoleId}", grantRoleId);
                    }
                    else
                    {
                        _logger.LogWarning("GrantPermissionToRole command missing required parameters");
                    }
                    break;

                case "revokepermissionfromrole":
                    if (TryGetValue<uint>(args, "roleId", out var revokeRoleId) &&
                        TryGetValue<uint>(args, "permissionId", out var revokePermissionId))
                    {
                        _logger.LogInformation("Processing RevokePermissionFromRole command for role: {RoleId}, permission: {PermissionId}", revokeRoleId, revokePermissionId);
                        reducers.RevokePermissionFromRole(revokeRoleId, revokePermissionId, null);
                        _logger.LogInformation("RevokePermissionFromRole command completed for role: {RoleId}", revokeRoleId);
                    }
                    else
                    {
                        _logger.LogWarning("RevokePermissionFromRole command missing required parameters");
                    }
                    break;

                case "removerole":
                    if (TryGetValue<Identity>(args, "userId", out var removeUserId) &&
                        TryGetValue<uint>(args, "roleId", out var removeRoleId))
                    {
                        _logger.LogInformation("Processing RemoveRole command for user: {UserId}, role: {RoleId}", removeUserId, removeRoleId);
                        reducers.RemoveRole(removeUserId, removeRoleId, null);
                        _logger.LogInformation("RemoveRole command completed for user: {UserId}", removeUserId);
                    }
                    else
                    {
                        _logger.LogWarning("RemoveRole command missing required parameters");
                    }
                    break;

                // Bus Management
                case "createbus":
                    if (TryGetValue<string>(args, "model", out var busModel))
                    {
                        string? registrationNumber = null;
                        if (args.ContainsKey("registrationNumber"))
                            registrationNumber = args["registrationNumber"].ToString();

                        _logger.LogInformation("Processing CreateBus command for model: {Model}, registration: {Registration}", busModel, registrationNumber ?? "None");
                        reducers.CreateBus(busModel, registrationNumber ?? "", null);
                        _logger.LogInformation("CreateBus command completed for model: {Model}", busModel);
                    }
                    else
                    {
                        _logger.LogWarning("CreateBus command missing required parameters");
                    }
                    break;

                case "updatebus":
                    if (TryGetValue<uint>(args, "busId", out var updateBusId))
                    {
                        string? model = null;
                        string? registrationNumber = null;

                        if (args.ContainsKey("model"))
                            model = args["model"].ToString();

                        if (args.ContainsKey("registrationNumber"))
                            registrationNumber = args["registrationNumber"].ToString();

                        _logger.LogInformation("Processing UpdateBus command for bus: {BusId}, model: {Model}, registration: {Registration}",
                            updateBusId, model ?? "Unchanged", registrationNumber ?? "Unchanged");
                        reducers.UpdateBus(updateBusId, model ?? "", registrationNumber ?? "", null);
                        _logger.LogInformation("UpdateBus command completed for bus: {BusId}", updateBusId);
                    }
                    else
                    {
                        _logger.LogWarning("UpdateBus command missing required parameters");
                    }
                    break;

                case "deletebus":
                    if (TryGetValue<uint>(args, "busId", out var deleteBusId))
                    {
                        _logger.LogInformation("Processing DeleteBus command for bus: {BusId}", deleteBusId);
                        reducers.DeleteBus(deleteBusId, null);
                        _logger.LogInformation("DeleteBus command completed for bus: {BusId}", deleteBusId);
                    }
                    else
                    {
                        _logger.LogWarning("DeleteBus command missing required parameters");
                    }
                    break;

                case "activatebus":
                    if (TryGetValue<uint>(args, "busId", out var activateBusId))
                    {
                        _logger.LogInformation("Processing ActivateBus command for bus: {BusId}", activateBusId);
                        reducers.ActivateBus(activateBusId, null);
                        _logger.LogInformation("ActivateBus command completed for bus: {BusId}", activateBusId);
                    }
                    else
                    {
                        _logger.LogWarning("ActivateBus command missing required parameters");
                    }
                    break;

                case "deactivatebus":
                    if (TryGetValue<uint>(args, "busId", out var deactivateBusId))
                    {
                        _logger.LogInformation("Processing DeactivateBus command for bus: {BusId}", deactivateBusId);
                        reducers.DeactivateBus(deactivateBusId, null);
                        _logger.LogInformation("DeactivateBus command completed for bus: {BusId}", deactivateBusId);
                    }
                    else
                    {
                        _logger.LogWarning("DeactivateBus command missing required parameters");
                    }
                    break;

                // Route Management
                case "createroute":
                    if (TryGetValue<string>(args, "startPoint", out var routeStartPoint) &&
                        TryGetValue<string>(args, "endPoint", out var routeEndPoint) &&
                        TryGetValue<uint>(args, "driverId", out var routeDriverId) &&
                        TryGetValue<uint>(args, "busId", out var routeBusId) &&
                        TryGetValue<string>(args, "travelTime", out var routeTravelTime) &&
                        TryGetValue<bool>(args, "isActive", out var routeIsActive))
                    {
                        _logger.LogInformation("Processing CreateRoute command from {Start} to {End}, driver: {DriverId}, bus: {BusId}",
                            routeStartPoint, routeEndPoint, routeDriverId, routeBusId);
                        reducers.CreateRoute(routeStartPoint, routeEndPoint, routeDriverId, routeBusId, routeTravelTime, routeIsActive);
                        _logger.LogInformation("CreateRoute command completed from {Start} to {End}", routeStartPoint, routeEndPoint);
                    }
                    else
                    {
                        _logger.LogWarning("CreateRoute command missing required parameters");
                    }
                    break;

                case "updateroute":
                    if (TryGetValue<uint>(args, "routeId", out var updateRouteId))
                    {
                        string? updStartPoint = null;
                        string? updEndPoint = null;
                        uint? updDriverId = null;
                        uint? updBusId = null;
                        string? updTravelTime = null;
                        bool? updIsActive = null;

                        if (args.ContainsKey("startPoint"))
                            updStartPoint = args["startPoint"].ToString();

                        if (args.ContainsKey("endPoint"))
                            updEndPoint = args["endPoint"].ToString();

                        if (args.ContainsKey("driverId"))
                            updDriverId = Convert.ToUInt32(args["driverId"]);

                        if (args.ContainsKey("busId"))
                            updBusId = Convert.ToUInt32(args["busId"]);

                        if (args.ContainsKey("travelTime"))
                            updTravelTime = args["travelTime"].ToString();

                        _logger.LogInformation("Processing UpdateRoute command for route: {RouteId}, start: {Start}, end: {End}",
                            updateRouteId, updStartPoint ?? "Unchanged", updEndPoint ?? "Unchanged");
                        reducers.UpdateRoute(updateRouteId, updStartPoint, updEndPoint, updDriverId, updBusId, updTravelTime, updIsActive, null);
                        _logger.LogInformation("UpdateRoute command completed for route: {RouteId}", updateRouteId);
                    }
                    else
                    {
                        _logger.LogWarning("UpdateRoute command missing required parameters");
                    }
                    break;

                case "deleteroute":
                    if (TryGetValue<uint>(args, "routeId", out var deleteRouteId))
                    {
                        _logger.LogInformation("Processing DeleteRoute command for route: {RouteId}", deleteRouteId);
                        reducers.DeleteRoute(deleteRouteId, null);
                        _logger.LogInformation("DeleteRoute command completed for route: {RouteId}", deleteRouteId);
                    }
                    else
                    {
                        _logger.LogWarning("DeleteRoute command missing required parameters");
                    }
                    break;

                case "activateroute":
                    if (TryGetValue<uint>(args, "routeId", out var activateRouteId))
                    {
                        _logger.LogInformation("Processing ActivateRoute command for route: {RouteId}", activateRouteId);
                        reducers.ActivateRoute(activateRouteId, null);
                        _logger.LogInformation("ActivateRoute command completed for route: {RouteId}", activateRouteId);
                    }
                    else
                    {
                        _logger.LogWarning("ActivateRoute command missing required parameters");
                    }
                    break;

                case "deactivateroute":
                    if (TryGetValue<uint>(args, "routeId", out var deactivateRouteId))
                    {
                        _logger.LogInformation("Processing DeactivateRoute command for route: {RouteId}", deactivateRouteId);
                        reducers.DeactivateRoute(deactivateRouteId, null);
                        _logger.LogInformation("DeactivateRoute command completed for route: {RouteId}", deactivateRouteId);
                    }
                    else
                    {
                        _logger.LogWarning("DeactivateRoute command missing required parameters");
                    }
                    break;

                // Schedule Management
                case "createrouteschedule":
                    if (TryGetValue<uint>(args, "routeId", out var scheduleRouteId) &&
                        TryGetValue<ulong>(args, "departureTime", out var scheduleDepartureTime) &&
                        TryGetValue<double>(args, "price", out var schedulePrice) &&
                        TryGetValue<uint>(args, "availableSeats", out var scheduleSeats) &&
                        TryGetValue<string[]>(args, "daysOfWeek", out var scheduleDaysArray) &&
                        TryGetValue<string[]>(args, "routeStops", out var scheduleRouteStopsArray) &&
                        TryGetValue<double[]>(args, "stopDistances", out var scheduleStopDistancesArray) &&
                        TryGetValue<string>(args, "startPoint", out var newStartPoint) &&
                        TryGetValue<string>(args, "endPoint", out var newEndPoint) &&
                        TryGetValue<ulong>(args, "arrivalTime", out var arrivalTime) &&
                        TryGetValue<uint>(args, "stopDurationMinutes", out var stopDurationMinutes) &&
                        TryGetValue<bool>(args, "isRecurring", out var isRecurring) &&
                        TryGetValue<string[]>(args, "estimatedStopTimes", out var estimatedStopTimesArray))
                    {
                        var scheduleDays = new List<string>(scheduleDaysArray);
                        var scheduleRouteStops = new List<string>(scheduleRouteStopsArray);
                        var scheduleStopDistances = new List<double>(scheduleStopDistancesArray);
                        var estimatedStopTimes = new List<string>(estimatedStopTimesArray);

                        string? notes = null; // Assuming you have a way to get this

                        _logger.LogInformation("Processing CreateRouteSchedule command for route: {RouteId}, stops: {StopCount}, price: {Price}",
                            scheduleRouteId, scheduleRouteStops.Count, schedulePrice);
                        reducers.CreateRouteSchedule(scheduleRouteId, scheduleDepartureTime, schedulePrice, scheduleSeats, scheduleDays, newStartPoint, newEndPoint, scheduleRouteStops, arrivalTime, stopDurationMinutes, isRecurring, estimatedStopTimes, scheduleStopDistances, notes);
                        _logger.LogInformation("CreateRouteSchedule command completed for route: {RouteId}", scheduleRouteId);
                    }
                    else
                    {
                        _logger.LogWarning("CreateRouteSchedule command missing required parameters");
                    }
                    break;

                case "updaterouteschedule":
                    if (TryGetValue<uint>(args, "scheduleId", out var updateScheduleId))
                    {
                        uint? updRouteId = null;
                        string? updStartPoint = null;
                        string? updEndPoint = null;
                        List<string>? updRouteStops = null;
                        ulong? updDepartureTime = null;
                        ulong? updArrivalTime = null;
                        double? updPrice = null;
                        uint? updAvailableSeats = null;
                        List<string>? updDaysOfWeek = null;
                        List<string>? updBusTypes = null;
                        uint? updStopDurationMinutes = null;
                        bool? updIsRecurring = null;
                        List<string>? updEstimatedStopTimes = null;
                        List<double>? updStopDistances = null;
                        string? updNotes = null;

                        if (args.ContainsKey("routeId"))
                            updRouteId = Convert.ToUInt32(args["routeId"]);

                        if (args.ContainsKey("startPoint"))
                            updStartPoint = args["startPoint"].ToString();

                        if (args.ContainsKey("endPoint"))
                            updEndPoint = args["endPoint"].ToString();

                        if (args.ContainsKey("routeStops") && args["routeStops"] is string[] routeStopsArray)
                            updRouteStops = new List<string>(routeStopsArray);

                        if (args.ContainsKey("departureTime"))
                            updDepartureTime = Convert.ToUInt64(args["departureTime"]);

                        if (args.ContainsKey("arrivalTime"))
                            updArrivalTime = Convert.ToUInt64(args["arrivalTime"]);

                        if (args.ContainsKey("price"))
                            updPrice = Convert.ToDouble(args["price"]);

                        if (args.ContainsKey("availableSeats"))
                            updAvailableSeats = Convert.ToUInt32(args["availableSeats"]);

                        if (args.ContainsKey("daysOfWeek") && args["daysOfWeek"] is string[] daysArray)
                            updDaysOfWeek = new List<string>(daysArray);

                        if (args.ContainsKey("busTypes") && args["busTypes"] is string[] busTypesArray)
                            updBusTypes = new List<string>(busTypesArray);

                        if (args.ContainsKey("stopDurationMinutes"))
                            updStopDurationMinutes = Convert.ToUInt32(args["stopDurationMinutes"]);

                        if (args.ContainsKey("isRecurring"))
                            updIsRecurring = Convert.ToBoolean(args["isRecurring"]);

                        if (args.ContainsKey("estimatedStopTimes") && args["estimatedStopTimes"] is string[] stopTimesArray)
                            updEstimatedStopTimes = new List<string>(stopTimesArray);

                        if (args.ContainsKey("stopDistances") && args["stopDistances"] is double[] distancesArray)
                            updStopDistances = new List<double>(distancesArray);

                        if (args.ContainsKey("notes"))
                            updNotes = args["notes"].ToString();

                        _logger.LogInformation("Processing UpdateRouteSchedule command for schedule: {ScheduleId}", updateScheduleId);
                        reducers.UpdateRouteSchedule(updateScheduleId, updRouteId, updStartPoint, updEndPoint, updRouteStops,
                            updDepartureTime, updArrivalTime, updPrice, updAvailableSeats, updDaysOfWeek, updBusTypes,
                            updStopDurationMinutes, updIsRecurring, updEstimatedStopTimes, updStopDistances, updNotes,null);
                    }
                    break;

                // Ticket Management
                case "createticket":
                    if (TryGetValue<uint>(args, "routeId", out var ticketRouteId) &&
                        TryGetValue<double>(args, "price", out var ticketPrice) &&
                        TryGetValue<uint>(args, "seatNumber", out var seatNumber)) // Added seatNumber
                    {
                        _logger.LogInformation("Processing CreateTicket command for route: {RouteId}, price: {Price}, seat: {SeatNumber}", ticketRouteId, ticketPrice, seatNumber);
                        reducers.CreateTicket(ticketRouteId, ticketPrice, seatNumber, null, null,null); // Added seatNumber and placeholders for other parameters
                    }
                    break;

                case "createsale":
                    if (TryGetValue<uint>(args, "ticketId", out var saleTicketId) &&
                        TryGetValue<string>(args, "buyerName", out var buyerName) &&
                        TryGetValue<string>(args, "buyerPhone", out var buyerPhone) &&
                        TryGetValue<string>(args, "saleLocation", out var saleLocation)) // Added saleLocation
                    {
                        _logger.LogInformation("Processing CreateSale command for ticket: {TicketId}, buyer: {BuyerName}, location: {SaleLocation}", saleTicketId, buyerName, saleLocation);
                        reducers.CreateSale(saleTicketId, buyerName, buyerPhone, saleLocation, null); // Added saleLocation and placeholder for other parameter
                    }
                    break;

                case "cancelticket":
                    if (TryGetValue<uint>(args, "ticketId", out var cancelTicketId))
                    {
                        _logger.LogInformation("Processing CancelTicket command for ticket: {TicketId}", cancelTicketId);
                        reducers.CancelTicket(cancelTicketId, null);
                    }
                    break;

                // Debug
                case "debugverifypassword":
                    if (TryGetValue<string>(args, "password", out var debugPassword) &&
                        TryGetValue<string>(args, "storedHash", out var debugStoredHash))
                    {
                        _logger.LogInformation("Processing DebugVerifyPassword command");
                        reducers.DebugVerifyPassword(debugPassword, debugStoredHash);
                    }
                    break;

                // Employee Management
                case "createemployee":
                    if (TryGetValue<string>(args, "employeeName", out var newEmpName) &&
                        TryGetValue<string>(args, "employeeSurname", out var newEmpSurname) &&
                        TryGetValue<string>(args, "employeePatronym", out var newEmpPatronym) &&
                        TryGetValue<uint>(args, "jobId", out var newEmpJobId))
                    {
                        _logger.LogInformation("Processing CreateEmployee command for: {Name} {Surname}", newEmpName, newEmpSurname);
                        reducers.CreateEmployee(newEmpName, newEmpSurname, newEmpPatronym, newEmpJobId);
                    }
                    break;

                case "updateemployee":
                    if (TryGetValue<uint>(args, "employeeId", out var updateEmpId))
                    {
                        string? updEmpName = null;
                        string? updEmpSurname = null;
                        string? updEmpPatronym = null;
                        uint? updEmpJobId = null;

                        if (args.ContainsKey("employeeName"))
                            updEmpName = args["employeeName"].ToString();

                        if (args.ContainsKey("employeeSurname"))
                            updEmpSurname = args["employeeSurname"].ToString();

                        if (args.ContainsKey("employeePatronym"))
                            updEmpPatronym = args["employeePatronym"].ToString();

                        if (args.ContainsKey("jobId"))
                            updEmpJobId = Convert.ToUInt32(args["jobId"]);

                        _logger.LogInformation("Processing UpdateEmployee command for employee: {EmployeeId}", updateEmpId);
                        reducers.UpdateEmployee(updateEmpId, updEmpName, updEmpSurname, updEmpPatronym, updEmpJobId, null);
                    }
                    break;

                case "deleteemployee":
                    if (TryGetValue<uint>(args, "employeeId", out var deleteEmpId))
                    {
                        _logger.LogInformation("Processing DeleteEmployee command for employee: {EmployeeId}", deleteEmpId);
                        reducers.DeleteEmployee(deleteEmpId, null);
                    }
                    break;

                // Job Management
                case "createjob":
                    if (TryGetValue<string>(args, "jobTitle", out var newJobTitle) &&
                        TryGetValue<string>(args, "jobInternship", out var newJobInternship))
                    {
                        _logger.LogInformation("Processing CreateJob command for title: {Title}", newJobTitle);
                        reducers.CreateJob(newJobTitle, newJobInternship);
                    }
                    break;

                case "updatejob":
                    if (TryGetValue<uint>(args, "jobId", out var updateJobId))
                    {
                        string? updJobTitle = null;
                        string? updJobInternship = null;

                        if (args.ContainsKey("jobTitle"))
                            updJobTitle = args["jobTitle"].ToString();

                        if (args.ContainsKey("internship"))
                            updJobInternship = args["internship"].ToString();

                        _logger.LogInformation("Processing UpdateJob command for job: {JobId}", updateJobId);
                        reducers.UpdateJob(updateJobId, updJobTitle, updJobInternship, null);
                    }
                    break;

                case "deletejob":
                    if (TryGetValue<uint>(args, "jobId", out var deleteJobId))
                    {
                        _logger.LogInformation("Processing DeleteJob command for job: {JobId}", deleteJobId);
                        reducers.DeleteJob(deleteJobId, null);
                    }
                    break;

                // Maintenance Management
                case "createmaintenance":
                    if (TryGetValue<uint>(args, "busId", out var maintBusId) &&
                        TryGetValue<ulong>(args, "lastServiceDate", out var maintLastService) &&
                        TryGetValue<string>(args, "serviceEngineer", out var maintEngineer) &&
                        TryGetValue<string>(args, "foundIssues", out var maintIssues) &&
                        TryGetValue<ulong>(args, "nextServiceDate", out var maintNextService) &&
                        TryGetValue<string>(args, "roadworthiness", out var maintRoadworthiness) &&
                        TryGetValue<string>(args, "maintenanceType", out var maintType))
                    {
                        _logger.LogInformation("Processing CreateMaintenance command for bus: {BusId}", maintBusId);
                        reducers.CreateMaintenance(maintBusId, maintLastService, maintEngineer, maintIssues,
                            maintNextService, maintRoadworthiness, maintType, null);
                    }
                    break;

                case "updatemaintenance":
                    if (TryGetValue<uint>(args, "maintenanceId", out var updateMaintId))
                    {
                        uint? updMaintBusId = null;
                        ulong? updMaintLastService = null;
                        string? updMaintEngineer = null;
                        string? updMaintIssues = null;
                        ulong? updMaintNextService = null;
                        string? updMaintRoadworthiness = null;
                        string? updMaintType = null;
                        string? updMaintMileage = null;

                        if (args.ContainsKey("busId"))
                            updMaintBusId = Convert.ToUInt32(args["busId"]);

                        if (args.ContainsKey("lastServiceDate"))
                            updMaintLastService = Convert.ToUInt64(args["lastServiceDate"]);

                        if (args.ContainsKey("serviceEngineer"))
                            updMaintEngineer = args["serviceEngineer"].ToString();

                        if (args.ContainsKey("foundIssues"))
                            updMaintIssues = args["foundIssues"].ToString();

                        if (args.ContainsKey("nextServiceDate"))
                            updMaintNextService = Convert.ToUInt64(args["nextServiceDate"]);

                        if (args.ContainsKey("roadworthiness"))
                            updMaintRoadworthiness = args["roadworthiness"].ToString();

                        if (args.ContainsKey("maintenanceType"))
                            updMaintType = args["maintenanceType"].ToString();

                        if (args.ContainsKey("mileage"))
                            updMaintMileage = args["mileage"].ToString();

                        _logger.LogInformation("Processing UpdateMaintenance command for maintenance: {MaintenanceId}", updateMaintId);
                        reducers.UpdateMaintenance(updateMaintId, updMaintBusId, updMaintLastService, updMaintEngineer,
                            updMaintIssues, updMaintNextService, updMaintRoadworthiness, updMaintType, updMaintMileage, null);
                    }
                    break;

                case "deletemaintenance":
                    if (TryGetValue<uint>(args, "maintenanceId", out var deleteMaintId))
                    {
                        _logger.LogInformation("Processing DeleteMaintenance command for maintenance: {MaintenanceId}", deleteMaintId);
                        reducers.DeleteMaintenance(deleteMaintId, null);
                    }
                    break;

                case "getbusmaintenancehistory":
                    if (TryGetValue<uint>(args, "busId", out var historyBusId))
                    {
                        _logger.LogInformation("Processing GetBusMaintenanceHistory command for bus: {BusId}", historyBusId);
                        reducers.GetBusMaintenanceHistory(historyBusId,null);
                    }
                    break;

                // Permission Management
                case "addnewpermission":
                    if (TryGetValue<string>(args, "name", out var permName) &&
                        TryGetValue<string>(args, "description", out var permDesc) &&
                        TryGetValue<string>(args, "category", out var permCategory))
                    {
                        _logger.LogInformation("Processing AddNewPermission command for: {Name}", permName);
                        reducers.AddNewPermission(permName, permDesc, permCategory, null);
                    }
                    break;

                case "updatepermission":
                    if (TryGetValue<uint>(args, "permissionId", out var updatePermId))
                    {
                        string? updPermName = null;
                        string? updPermDesc = null;
                        string? updPermCategory = null;
                        bool? updPermIsActive = null;

                        if (args.ContainsKey("name"))
                            updPermName = args["name"].ToString();

                        if (args.ContainsKey("description"))
                            updPermDesc = args["description"].ToString();

                        if (args.ContainsKey("category"))
                            updPermCategory = args["category"].ToString();

                        if (args.ContainsKey("isActive"))
                            updPermIsActive = Convert.ToBoolean(args["isActive"]);

                        _logger.LogInformation("Processing UpdatePermission command for permission: {PermissionId}", updatePermId);
                        reducers.UpdatePermission(updatePermId, updPermName, updPermDesc, updPermCategory, updPermIsActive, null);
                    }
                    break;

                case "deletepermission":
                    if (TryGetValue<uint>(args, "permissionId", out var deletePermId))
                    {
                        _logger.LogInformation("Processing DeletePermission command for permission: {PermissionId}", deletePermId);
                        reducers.DeletePermission(deletePermId, null);
                    }
                    break;

                // Role Management
                case "createrole":
                    if (TryGetValue<int>(args, "legacyRoleId", out var newRoleLegacyId) &&
                        TryGetValue<string>(args, "name", out var newRoleName) &&
                        TryGetValue<string>(args, "description", out var newRoleDesc) &&
                        TryGetValue<bool>(args, "isSystem", out var newRoleIsSystem) &&
                        TryGetValue<uint>(args, "priority", out var newRolePriority))
                    {
                        _logger.LogInformation("Processing CreateRole command for: {Name}", newRoleName);
                        reducers.CreateRoleReducer(newRoleLegacyId, newRoleName, newRoleDesc, newRoleIsSystem, newRolePriority, null);
                    }
                    break;

                case "updaterole":
                    if (TryGetValue<uint>(args, "roleId", out var updateRoleId))
                    {
                        string? updRoleName = null;
                        string? updRoleDesc = null;
                        int? updRoleLegacyId = null;
                        uint? updRolePriority = null;

                        if (args.ContainsKey("name"))
                            updRoleName = args["name"].ToString();

                        if (args.ContainsKey("description"))
                            updRoleDesc = args["description"].ToString();

                        if (args.ContainsKey("legacyRoleId"))
                            updRoleLegacyId = Convert.ToInt32(args["legacyRoleId"]);

                        if (args.ContainsKey("priority"))
                            updRolePriority = Convert.ToUInt32(args["priority"]);

                        _logger.LogInformation("Processing UpdateRole command for role: {RoleId}", updateRoleId);
                        reducers.UpdateRole(updateRoleId, updRoleName, updRoleDesc, updRoleLegacyId, updRolePriority, null);
                    }
                    break;

                case "deleterole":
                    if (TryGetValue<uint>(args, "roleId", out var deleteRoleId))
                    {
                        _logger.LogInformation("Processing DeleteRole command for role: {RoleId}", deleteRoleId);
                        reducers.DeleteRole(deleteRoleId, null); // null for acting user
                    }
                    break;

                // User Management
                case "changepassword":
                    if (TryGetValue<Identity>(args, "userId", out var pwdUserId) &&
                        TryGetValue<string>(args, "currentPassword", out var currentPwd) &&
                        TryGetValue<string>(args, "newPassword", out var newPwd))
                    {
                        _logger.LogInformation("Processing ChangePassword command for user: {UserId}", pwdUserId);
                        reducers.ChangePassword(pwdUserId, currentPwd, newPwd, null); // null for acting user
                    }
                    break;

                case "claimuseraccount":
                    if (TryGetValue<string>(args, "login", out var claimLogin) &&
                        TryGetValue<string>(args, "password", out var claimPassword))
                    {
                        _logger.LogInformation("Processing ClaimUserAccount command for login: {Login}", claimLogin);
                        reducers.ClaimUserAccount(claimLogin, claimPassword,null);
                    }
                    break;

                case "activateuser":
                    if (TryGetValue<Identity>(args, "userId", out var activateUserId))
                    {
                        _logger.LogInformation("Processing ActivateUser command for user: {UserId}", activateUserId);
                        reducers.ActivateUser(activateUserId, null);
                    }
                    break;

                case "deactivateuser":
                    if (TryGetValue<Identity>(args, "userId", out var deactivateUserId))
                    {
                        _logger.LogInformation("Processing DeactivateUser command for user: {UserId}", deactivateUserId);
                        reducers.DeactivateUser(deactivateUserId, null);
                    }
                    break;

                case "deleteuser":
                    if (TryGetValue<Identity>(args, "userId", out var deleteUserId))
                    {
                        _logger.LogInformation("Processing DeleteUser command for user: {UserId}", deleteUserId);
                        reducers.DeleteUser(deleteUserId, null);
                    }
                    break;

                case "updateuser":
                    if (TryGetValue<Identity>(args, "userId", out var updateUserId))
                    {
                        string? updUserLogin = null;
                        string? updUserPwdHash = null;
                        int? updUserRole = null;
                        string? updUserPhone = null;
                        string? updUserEmail = null;
                        bool? updUserIsActive = null;

                        if (args.ContainsKey("login"))
                            updUserLogin = args["login"].ToString();

                        if (args.ContainsKey("passwordHash"))
                            updUserPwdHash = args["passwordHash"].ToString();

                        if (args.ContainsKey("role"))
                            updUserRole = int.TryParse(args["role"].ToString(), out var role) ? role : (int?)null;

                        if (args.ContainsKey("phoneNumber"))
                            updUserPhone = args["phoneNumber"].ToString();

                        if (args.ContainsKey("email"))
                            updUserEmail = args["email"].ToString();

                        if (args.ContainsKey("isActive"))
                            updUserIsActive = Convert.ToBoolean(args["isActive"]);

                        _logger.LogInformation("Processing UpdateUser command for user: {UserId}", updateUserId);
                        reducers.UpdateUser(updateUserId, updUserLogin, updUserPwdHash, updUserRole,
                            updUserPhone, updUserEmail, updUserIsActive, null);
                    }
                    break;

                // Admin Actions
                case "logadminaction":
                    if (TryGetValue<string>(args, "userId", out var logUserId) &&
                        TryGetValue<string>(args, "action", out var logAction) &&
                        TryGetValue<string>(args, "details", out var logDetails) &&
                        TryGetValue<string>(args, "timestamp", out var logTimestamp) &&
                        TryGetValue<string>(args, "ipAddress", out var logIpAddress) &&
                        TryGetValue<string>(args, "userAgent", out var logUserAgent))
                    {
                        _logger.LogInformation("Processing LogAdminAction command for user: {UserId}, action: {Action}", logUserId, logAction);
                        reducers.LogAdminAction(logUserId, logAction, logDetails, logTimestamp, logIpAddress, logUserAgent, null);
                    }
                    break;

                // Default case for unknown commands
                default:
                    _logger.LogWarning("Unknown command: {Command}", command);
                    break;
            }
        }

        // Helper method to safely get values from the args dictionary
        private bool TryGetValue<T>(Dictionary<string, object> args, string key, out T value)
        {
            value = default!;

            if (!args.ContainsKey(key))
                return false;

            try
            {
                if (typeof(T) == typeof(string))
                {
                    value = (T)(object)args[key].ToString()!;
                    return true;
                }
                else if (typeof(T) == typeof(uint))
                {
                    value = (T)(object)Convert.ToUInt32(args[key]);
                    return true;
                }
                else if (typeof(T) == typeof(int))
                {
                    value = (T)(object)Convert.ToInt32(args[key]);
                    return true;
                }
                else if (typeof(T) == typeof(double))
                {
                    value = (T)(object)Convert.ToDouble(args[key]);
                    return true;
                }
                else if (typeof(T) == typeof(ulong))
                {
                    value = (T)(object)Convert.ToUInt64(args[key]);
                    return true;
                }
                else if (typeof(T) == typeof(bool))
                {
                    value = (T)(object)Convert.ToBoolean(args[key]);
                    return true;
                }
                else if (typeof(T) == typeof(Identity))
                {
                    if (args[key] is Identity identity)
                    {
                        value = (T)(object)identity;
                        return true;
                    }
                    return false;
                }
                else
                {
                    value = (T)args[key];
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // Callback method for successful connection
        private void OnConnected(DbConnection conn, Identity identity, string token)
        {
            try
            {
                _logger.LogInformation("Connected to SpacetimeDB with identity: {Identity}", identity);
                _localIdentity = identity;
                AuthToken.SaveToken(token);

                // Subscribe to all regular tables (event tables are excluded)
                conn.SubscriptionBuilder()
                    .OnApplied(OnSubscriptionApplied)
                    .OnError(OnSubscriptionError)
                    .SubscribeToAllTables();

                // Subscribe to event tables explicitly (they are excluded from SubscribeToAllTables)
                _logger.LogInformation("Subscribing to event tables");
                conn.SubscriptionBuilder()
                    .OnApplied(ctx => _logger.LogInformation("Event table subscription applied successfully"))
                    .OnError((ctx, err) => _logger.LogError(err, "Error subscribing to event tables: {ErrorMessage}", err.Message))
                    .Subscribe(new[] {
                        "SELECT * FROM AuthenticationEvent",
                        "SELECT * FROM TicketSaleEvent",
                        "SELECT * FROM BusStatusEvent",
                        "SELECT * FROM RouteScheduleEvent",
                        "SELECT * FROM MaintenanceEvent"
                    });

                // Register event table callbacks
                RegisterEventTableCallbacks(conn);

                _isConnecting = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnected callback");
                _isConnecting = false;
                throw;
            }
        }

        // Callback method for connection errors
        private void OnConnectError(Exception ex)
        {
            // Log the connection error
            _logger.LogError(ex, "Error connecting to SpacetimeDB: {ErrorMessage}", ex.Message);
            _connection = null; // Reset the connection
            _localIdentity = null; // Reset the local identity
            _isConnecting = false;
        }

        // Callback method for disconnection
        private void OnDisconnected(DbConnection conn, Exception? ex)
        {
            if (ex != null)
            {
                // Log the disconnection due to an error
                _logger.LogError(ex, "Disconnected from SpacetimeDB due to error: {ErrorMessage}", ex.Message);
            }
            else
            {
                // Log the normal disconnection
                _logger.LogInformation("Disconnected from SpacetimeDB");
            }
            _connection = null; // Reset the connection
            _localIdentity = null; // Reset the local identity
            _isConnecting = false;
        }

        // Callback method for subscription application
        private void OnSubscriptionApplied(SubscriptionEventContext ctx)
        {
            try
            {
                // Log the successful subscription application
                _logger.LogInformation("SpacetimeDB subscription applied successfully");
                _logger.LogDebug("Database tables are now available in the client cache");
                _subscriptionApplied = true; // Set the flag to indicate subscription is ready
            }
            catch (Exception ex)
            {
                // Log any errors in the on-subscription-applied callback
                _logger.LogError(ex, "Error in OnSubscriptionApplied callback: {ErrorMessage}", ex.Message);
                throw; // Rethrow the exception
            }
        }

        // Callback method for subscription errors
        private void OnSubscriptionError(ErrorContext ctx, Exception ex)
        {
            _logger.LogError(ex, "Error in subscription: {ErrorMessage}", ex.Message);
        }

        // ***** Event Table Callback Registration *****
        // Register callbacks for all event tables to handle cross-client notifications

        /// <summary>
        /// Registers callbacks for all event tables
        /// </summary>
        private void RegisterEventTableCallbacks(DbConnection conn)
        {
            _logger.LogInformation("Registering event table callbacks");

            // Register AuthenticationEvent callback
            conn.Db.AuthenticationEvent.OnInsert += OnAuthenticationEvent;

            // Register TicketSaleEvent callback
            conn.Db.TicketSaleEvent.OnInsert += OnTicketSaleEvent;

            // Register BusStatusEvent callback
            conn.Db.BusStatusEvent.OnInsert += OnBusStatusEvent;

            // Register RouteScheduleEvent callback
            conn.Db.RouteScheduleEvent.OnInsert += OnRouteScheduleEvent;

            // Register MaintenanceEvent callback
            conn.Db.MaintenanceEvent.OnInsert += OnMaintenanceEvent;

            _logger.LogInformation("Event table callbacks registered successfully");
        }

        // ***** Event Table Handlers *****

        /// <summary>
        /// Handles AuthenticationEvent notifications
        /// Logs authentication events (Login, Logout, Failed)
        /// </summary>
        private void OnAuthenticationEvent(EventContext ctx, AuthenticationEvent evt)
        {
            try
            {
                // Check the event context to understand what caused this event
                string eventSource = ctx.Event switch
                {
                    Event<Reducer>.Reducer => "OwnReducer",
                    Event<Reducer>.SubscribeApplied => "SubscribeApplied",
                    Event<Reducer>.UnsubscribeApplied => "UnsubscribeApplied",
                    Event<Reducer>.SubscribeError => "SubscribeError",
                    _ => "Unknown"
                };

                _logger.LogDebug("Authentication event from {EventSource}", eventSource);

                _logger.LogInformation("Authentication event received: Type={EventType}, UserId={UserId}, Timestamp={Timestamp}, Source={EventSource}",
                    evt.EventType, evt.UserId, evt.Timestamp, eventSource);

                switch (evt.EventType)
                {
                    case "Login":
                        _logger.LogInformation("User logged in: {UserId} from {IpAddress}",
                            evt.UserId, evt.IpAddress ?? "Unknown");
                        break;

                    case "Logout":
                        _logger.LogInformation("User logged out: {UserId}", evt.UserId);
                        break;

                    case "Failed":
                        _logger.LogWarning("Authentication failed: {UserId}, Details: {Details}",
                            evt.UserId, evt.Details ?? "No details");
                        break;

                    case "TokenRefresh":
                        _logger.LogInformation("Token refreshed for user: {UserId}", evt.UserId);
                        break;

                    default:
                        _logger.LogWarning("Unknown authentication event type: {EventType}", evt.EventType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling AuthenticationEvent: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// Handles TicketSaleEvent notifications
        /// Updates UI with ticket sale information
        /// </summary>
        private void OnTicketSaleEvent(EventContext ctx, TicketSaleEvent evt)
        {
            try
            {
                // Check the event context to understand what caused this event
                string eventSource = ctx.Event switch
                {
                    Event<Reducer>.Reducer => "OwnReducer",
                    Event<Reducer>.SubscribeApplied => "SubscribeApplied",
                    Event<Reducer>.UnsubscribeApplied => "UnsubscribeApplied",
                    Event<Reducer>.SubscribeError => "SubscribeError",
                    _ => "Unknown"
                };

                _logger.LogDebug("Ticket sale event from {EventSource}", eventSource);

                _logger.LogInformation("Ticket sale event received: SaleId={SaleId}, TicketId={TicketId}, RouteId={RouteId}, Amount={Amount}, PaymentMethod={PaymentMethod}, Source={EventSource}",
                    evt.SaleId, evt.TicketId, evt.RouteId, evt.Amount, evt.PaymentMethod, eventSource);

                // Log the sale details
                _logger.LogInformation("Ticket sold: SaleId={SaleId}, Buyer={BuyerId}, Amount={Amount:C}, Payment={PaymentMethod}",
                    evt.SaleId, evt.BuyerId, evt.Amount, evt.PaymentMethod);

                // TODO: Update UI with sale information
                // This could trigger a notification to the UI layer or update a local cache
                // For now, we just log the event
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling TicketSaleEvent: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// Handles BusStatusEvent notifications
        /// Updates UI with bus status changes
        /// </summary>
        private void OnBusStatusEvent(EventContext ctx, BusStatusEvent evt)
        {
            try
            {
                // Check the event context to understand what caused this event
                string eventSource = ctx.Event switch
                {
                    Event<Reducer>.Reducer => "OwnReducer",
                    Event<Reducer>.SubscribeApplied => "SubscribeApplied",
                    Event<Reducer>.UnsubscribeApplied => "UnsubscribeApplied",
                    Event<Reducer>.SubscribeError => "SubscribeError",
                    _ => "Unknown"
                };

                _logger.LogDebug("Bus status event from {EventSource}", eventSource);

                _logger.LogInformation("Bus status event received: BusId={BusId}, PreviousStatus={PreviousStatus}, NewStatus={NewStatus}, ChangedBy={ChangedBy}, Source={EventSource}",
                    evt.BusId, evt.PreviousStatus, evt.NewStatus, evt.ChangedBy, eventSource);

                // Log the status change
                _logger.LogInformation("Bus {BusId} status changed from {PreviousStatus} to {NewStatus} by {ChangedBy}. Reason: {Reason}",
                    evt.BusId, evt.PreviousStatus, evt.NewStatus, evt.ChangedBy, evt.Reason ?? "Not specified");

                // TODO: Update UI with bus status
                // This could trigger a notification to the UI layer or update a local cache
                // For now, we just log the event
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling BusStatusEvent: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// Handles RouteScheduleEvent notifications
        /// Updates UI with schedule change information
        /// </summary>
        private void OnRouteScheduleEvent(EventContext ctx, RouteScheduleEvent evt)
        {
            try
            {
                // Check the event context to understand what caused this event
                string eventSource = ctx.Event switch
                {
                    Event<Reducer>.Reducer => "OwnReducer",
                    Event<Reducer>.SubscribeApplied => "SubscribeApplied",
                    Event<Reducer>.UnsubscribeApplied => "UnsubscribeApplied",
                    Event<Reducer>.SubscribeError => "SubscribeError",
                    _ => "Unknown"
                };

                _logger.LogDebug("Route schedule event from {EventSource}", eventSource);

                _logger.LogInformation("Route schedule event received: ScheduleId={ScheduleId}, RouteId={RouteId}, EventType={EventType}, ChangedBy={ChangedBy}, Source={EventSource}",
                    evt.ScheduleId, evt.RouteId, evt.EventType, evt.ChangedBy, eventSource);

                // Log the schedule change
                _logger.LogInformation("Route schedule {ScheduleId} for route {RouteId} was {EventType} by {ChangedBy}",
                    evt.ScheduleId, evt.RouteId, evt.EventType, evt.ChangedBy);

                // TODO: Update UI with schedule information
                // This could trigger a notification to the UI layer or update a local cache
                // For now, we just log the event
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling RouteScheduleEvent: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// Handles MaintenanceEvent notifications
        /// Updates UI with maintenance status
        /// </summary>
        private void OnMaintenanceEvent(EventContext ctx, MaintenanceEvent evt)
        {
            try
            {
                // Check the event context to understand what caused this event
                string eventSource = ctx.Event switch
                {
                    Event<Reducer>.Reducer => "OwnReducer",
                    Event<Reducer>.SubscribeApplied => "SubscribeApplied",
                    Event<Reducer>.UnsubscribeApplied => "UnsubscribeApplied",
                    Event<Reducer>.SubscribeError => "SubscribeError",
                    _ => "Unknown"
                };

                _logger.LogDebug("Maintenance event from {EventSource}", eventSource);

                _logger.LogInformation("Maintenance event received: MaintenanceId={MaintenanceId}, BusId={BusId}, EventType={EventType}, ChangedBy={ChangedBy}, Source={EventSource}",
                    evt.MaintenanceId, evt.BusId, evt.EventType, evt.ChangedBy, eventSource);

                // Log the maintenance event
                _logger.LogInformation("Maintenance {MaintenanceId} for bus {BusId} was {EventType} by {ChangedBy}",
                    evt.MaintenanceId, evt.BusId, evt.EventType, evt.ChangedBy);

                // TODO: Update UI with maintenance status
                // This could trigger a notification to the UI layer or update a local cache
                // For now, we just log the event
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MaintenanceEvent: {ErrorMessage}", ex.Message);
            }
        }
    }
}