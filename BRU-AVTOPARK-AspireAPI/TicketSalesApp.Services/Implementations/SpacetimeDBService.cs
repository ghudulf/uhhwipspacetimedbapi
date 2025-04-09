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

        // Constructor to initialize configuration and logger
        public SpacetimeDBService(IConfiguration configuration, ILogger<SpacetimeDBService> logger)
        {
            _configuration = configuration; // Assign configuration
            _logger = logger; // Assign logger
            _inputQueue = new ConcurrentQueue<(string Command, Dictionary<string, object> Args)>(); // Initialize the input queue
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

                // Retrieve host and module name from configuration
                var host = _configuration["SpacetimeDB:Host"] ?? "http://localhost:3000"; // Default host
                var moduleName = _configuration["SpacetimeDB:ModuleName"] ?? "avtopark"; // Default module name

                // Log the connection attempt
                _logger.LogInformation("Connecting to SpacetimeDB at {Host} module {Module}", host, moduleName);

                // Initialize authentication token storage
                AuthToken.Init(".spacetime_csharp_avtopark");
                _logger.LogDebug("Authentication token initialized");

                // Build the database connection with necessary callbacks
                _logger.LogDebug("Building database connection with callbacks");
                _connection = DbConnection.Builder()
                        .WithUri(host) // Set the URI for the connection
                        .WithModuleName(moduleName) // Set the module name
                        .WithToken(AuthToken.Token) // Set the authentication token
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing frame tick: {ErrorMessage}", ex.Message);
                // Don't rethrow as this would break the application flow
            }
        }

        // Method to subscribe to all tables
        public void SubscribeToAllTables()
        {
            if (_connection == null)
            {
                _logger.LogError("Attempted to subscribe to all tables without an active connection");
                throw new InvalidOperationException("SpacetimeDB connection not initialized or not yet established. Call Connect() first and wait for connection to complete.");
            }

            _logger.LogInformation("Subscribing to all tables");
            _connection.SubscriptionBuilder()
                .OnApplied(OnSubscriptionApplied)
                .OnError(OnSubscriptionError)
                .SubscribeToAllTables();

            _logger.LogInformation("Subscribed to all tables successfully");
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
                        reducers.RegisterUser(login, password, email, phoneNumber, roleId, roleName);
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
                        reducers.ClaimUserAccount(claimLogin, claimPassword);
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

                // Subscribe to all tables
                conn.SubscriptionBuilder()
                    .OnApplied(OnSubscriptionApplied)
                    .OnError(OnSubscriptionError)
                    .SubscribeToAllTables();

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
    }
}