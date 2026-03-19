using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class WebSocketDebugViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly ApiClientService _apiClient;
    private readonly TokenStorageService _tokenStorage;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:5000";

    [ObservableProperty]
    private string _accessToken = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isInteractiveConnected;

    /// <summary>
    /// Indicates whether either WebSocket connection is active.
    /// </summary>
    public bool IsAnyConnected => IsConnected || IsInteractiveConnected;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private ObservableCollection<ControllerTestResult> _testResults = new();

    // Notify IsAnyConnected when either connection state changes
    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyConnected));
    }

    partial void OnIsInteractiveConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyConnected));
    }

    [ObservableProperty]
    private ObservableCollection<string> _eventLog = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<string, ControllerTestResult> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingCompletions = new();
    private int _cleanupInProgress = 0;
    // Serializes connect and cleanup paths to prevent _cts/_webSocket mutation races.
    private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

    // Shared handshake timeout used for all ConnectAsync calls.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(1000);

    // Dedicated socket for /api/realtime/interactive commands (echo, time, stats, help, calculate, stream:*)
    private ClientWebSocket? _interactiveWebSocket;
    private CancellationTokenSource? _interactiveCts;
    private readonly SemaphoreSlim _interactiveSendLock = new SemaphoreSlim(1, 1);
    // Serializes concurrent calls to GetOrConnectInteractiveSocketAsync.
    private readonly SemaphoreSlim _interactiveLock = new SemaphoreSlim(1, 1);
    // Single background receive loop dispatches frames to per-request TCS objects.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _interactivePending = new();
    // Tracks active stream IDs returned by stream:started responses so the UI can stop them.
    private readonly ConcurrentDictionary<string, string> _activeStreamIds = new(); // streamId -> resource

    private readonly Dictionary<string, string> _controllerEndpoints = new()
    {
        { "buses", "/api/buses/realtime/ws" },
        { "employees", "/api/employees/realtime/ws" },
        { "jobs", "/api/jobs/realtime/ws" },
        { "maintenance", "/api/maintenance/realtime/ws" },
        { "permissions", "/api/permissions/realtime/ws" },
        { "roles", "/api/roles/realtime/ws" },
        { "routes", "/api/routes/realtime/ws" },
        { "routeschedules", "/api/routeschedules/realtime/ws" },
        { "tickets", "/api/tickets/realtime/ws" },
        { "ticketsales", "/api/ticketsales/realtime/ws" },
        { "users", "/api/users/realtime/ws" }
    };

    public WebSocketDebugViewModel()
    {
        _apiClient = ApiClientService.Instance;
        _tokenStorage = new TokenStorageService();

        InitializeTestResults();
        LoadAuthenticationData().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                Log.Error(t.Exception, "Failed to load authentication data in WebSocketDebugViewModel");
            }
        }, TaskScheduler.Default);
    }

    private async Task LoadAuthenticationData()
    {
        try
        {
            var tokens = await _tokenStorage.GetTokensAsync();
            if (tokens != null && !string.IsNullOrEmpty(tokens.AccessToken))
            {
                AccessToken = tokens.AccessToken;
                AddLog("✓ Loaded access token from storage");
            }
            else
            {
                AddLog("⚠ No access token found - authentication may be required");
            }

            // Get server URL from API client configuration
            var httpClient = _apiClient.CreateClient();
            if (httpClient.BaseAddress != null)
            {
                // Remove /api suffix if present to get the base server URL
                var baseUrl = httpClient.BaseAddress.ToString().TrimEnd('/');
                if (baseUrl.EndsWith("/api"))
                {
                    baseUrl = baseUrl.Substring(0, baseUrl.Length - 4);
                }
                ServerUrl = baseUrl;
                AddLog($"✓ Using API server: {ServerUrl}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Error loading authentication data: {ex.Message}");
        }
    }

    private void InitializeTestResults()
    {
        TestResults.Clear();
        foreach (var kvp in _controllerEndpoints)
        {
            TestResults.Add(new ControllerTestResult
            {
                ControllerName = kvp.Key,
                EndpointPath = kvp.Value,
                Status = TestStatus.NotTested,
                Message = "Not tested yet"
            });
        }
    }

    /// <summary>
    /// Normalizes an access token by trimming whitespace and removing "Bearer " prefix if present.
    /// </summary>
    private static string NormalizeAccessToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;
        
        token = token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(7).Trim();
        }
        return token;
    }

    /// <summary>
    /// Builds a WebSocket URI from an HTTP/HTTPS server URI with proper scheme selection.
    /// Forces wss:// for non-loopback hosts, allows ws:// only for localhost/loopback.
    /// </summary>
    private static (bool success, Uri? wsUri, string? errorMessage) BuildWebSocketUri(Uri serverUri, string path)
    {
        string wsScheme;
        if (serverUri.Scheme == "https")
        {
            wsScheme = "wss";
        }
        else if (serverUri.Scheme == "http")
        {
            // Only allow ws:// for localhost/loopback
            if (serverUri.IsLoopback || 
                serverUri.Host == "localhost" || 
                serverUri.Host == "127.0.0.1" || 
                serverUri.Host == "[::1]")
            {
                wsScheme = "ws";
            }
            else
            {
                wsScheme = "wss"; // Force secure for remote hosts
            }
        }
        else
        {
            return (false, null, $"Unsupported URL scheme: {serverUri.Scheme}");
        }

        var wsUri = new UriBuilder(serverUri)
        {
            Scheme = wsScheme,
            Path = path
        }.Uri;

        return (true, wsUri, null);
    }

    [RelayCommand]
    private async Task ConnectWebSocket()
    {
        try
        {
            if (IsConnected)
            {
                await DisconnectWebSocket();
                return;
            }

            StatusMessage = "Connecting...";
            AddLog("Attempting to connect to WebSocket...");

            // Validate URL before allocating resources
            Uri serverUri;
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out serverUri!))
            {
                StatusMessage = "Invalid server URL";
                AddLog("✗ Invalid server URL format");
                return;
            }

            // Prefer existing AccessToken (textbox value) if non-empty, otherwise load from storage
            string? accessToken = AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                var tokens = await _tokenStorage.GetTokensAsync();
                if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
                {
                    StatusMessage = "Authentication required";
                    AddLog("✗ No access token available. Please login first.");
                    return;
                }
                accessToken = tokens.AccessToken;
            }
            
            // Normalize token: remove "Bearer " prefix if present (case-insensitive)
            accessToken = NormalizeAccessToken(accessToken);
            
            // Validate token is not empty after normalization
            if (string.IsNullOrEmpty(accessToken))
            {
                StatusMessage = "Invalid access token";
                AddLog("✗ Access token is empty after normalization. Please login first.");
                return;
            }
            
            AccessToken = accessToken; // Store normalized token

            // Validate URL scheme BEFORE allocating resources
            var buildResult = BuildWebSocketUri(serverUri, "/api/realtime/stream");
            if (!buildResult.success)
            {
                StatusMessage = buildResult.errorMessage ?? "Invalid URL";
                AddLog($"✗ {buildResult.errorMessage}");
                return;
            }

            // Now allocate resources after validation passed — hold lifecycle lock to prevent
            // a concurrent CleanupWebSocketAsync from disposing _cts/_webSocket mid-creation.
            // Keep lock held until after ConnectAsync completes.
            await _lifecycleLock.WaitAsync();
            try
            {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.AddSubProtocol("bru.events.v1");
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            var wsUrl = buildResult.wsUri!;
            AddLog($"Connecting to {wsUrl}...");

            // Add timeout to prevent indefinite hangs
            using var timeoutCts = new CancellationTokenSource(HandshakeTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);
            await _webSocket.ConnectAsync(wsUrl, linkedCts.Token);

            IsConnected = true;
            StatusMessage = "Connected";
            AddLog($"✓ Connected successfully");
            }
            finally
            {
                _lifecycleLock.Release();
            }

            // Capture _webSocket and _cts to prevent old tasks from tearing down new connections
            var capturedWebSocket = _webSocket;
            var capturedCts = _cts;

            // Start receiving messages with explicit exception handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReceiveMessagesAsync(capturedWebSocket, capturedCts);
                }
                catch (Exception ex)
                {
                    AddLog($"✗ ReceiveMessagesAsync failed: {ex.Message}");
                    Log.Error(ex, "ReceiveMessagesAsync failed");
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            AddLog($"✗ Connection failed: {ex.Message}");
            await CleanupWebSocketAsync();
        }
    }

    [RelayCommand]
    private async Task DisconnectWebSocket()
    {
        await CleanupWebSocketAsync();
        AddLog("Disconnected from WebSocket");
    }

    private async Task CleanupWebSocketAsync()
    {
        // Prevent concurrent cleanup using Interlocked guard
        if (Interlocked.Exchange(ref _cleanupInProgress, 1) == 1)
        {
            // Another cleanup is already in progress
            return;
        }

        try
        {
            // Cancel _cts early so ConnectAsync can be interrupted
            _cts?.Cancel();

            // Attempt polite close with short timeout
            if (_webSocket?.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnect", closeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timeout - fall back to abort
                    _webSocket?.Abort();
                }
                catch
                {
                    // Any other error - fall back to abort
                    _webSocket?.Abort();
                }
            }

            // Dispose cancellation token source and WebSocket
            await _lifecycleLock.WaitAsync();
            try
            {
                _cts?.Dispose();
                _cts = null;

                // Dispose WebSocket
                _webSocket?.Dispose();
                _webSocket = null;
            }
            finally
            {
                _lifecycleLock.Release();
            }

            // Cancel and clear pending completions
            foreach (var kvp in _pendingCompletions)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingCompletions.Clear();

            // Clear pending requests
            _pendingRequests.Clear();

            // Update UI state
            IsConnected = false;
            StatusMessage = "Disconnected";
        }
        catch (Exception ex)
        {
            AddLog($"Error during cleanup: {ex.Message}");
            IsConnected = false;
            StatusMessage = "Disconnected (with errors)";
        }
        finally
        {
            // Reset guard
            Interlocked.Exchange(ref _cleanupInProgress, 0);
        }
    }

    [RelayCommand]
    private async Task TestAllControllers()
    {
        if (!IsConnected)
        {
            AddLog("=== Starting controller-specific endpoint tests ===");
            StatusMessage = "Testing all controllers (direct endpoints)...";

            foreach (var result in TestResults)
            {
                await TestControllerSpecificEndpointWithCrud(result);
                await Task.Delay(500); // Delay between tests to avoid overwhelming the server
            }
        }
        else
        {
            AddLog("=== Starting tests via universal stream ===");
            StatusMessage = "Testing all controllers (universal stream)...";

            foreach (var result in TestResults)
            {
                await TestControllerViaUniversalStream(result);
                await Task.Delay(500); // Delay between tests
            }
        }

        var passed = TestResults.Count(r => r.Status == TestStatus.Passed);
        var failed = TestResults.Count(r => r.Status == TestStatus.Failed);
        StatusMessage = $"Tests complete: {passed} passed, {failed} failed";
        AddLog($"=== All tests complete: {passed}/{TestResults.Count} passed ===");
    }

    [RelayCommand]
    private async Task TestSingleController(ControllerTestResult result)
    {
        if (!IsConnected)
        {
            // Test controller-specific endpoint directly
            await TestControllerSpecificEndpointWithCrud(result);
        }
        else
        {
            // Use universal stream endpoint
            result.Status = TestStatus.Testing;
            result.Message = "Testing via universal stream...";
            await TestControllerViaUniversalStream(result);
        }
    }

    private async Task TestControllerViaUniversalStream(ControllerTestResult result)
    {
        try
        {
            const int DEFAULT_PAGE = 1;
            const int DEFAULT_PAGE_SIZE = 50;
            
            var requestId = Guid.NewGuid().ToString();
            
            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            // Build request object with conditional pagination
            var request = new Dictionary<string, object>
            {
                ["command"] = "read_all",
                ["requestId"] = requestId,
                ["resource"] = result.ControllerName
            };
            
            // Add pagination for routeschedules to prevent memory issues
            if (result.ControllerName == "routeschedules")
            {
                request["page"] = DEFAULT_PAGE;
                request["pageSize"] = DEFAULT_PAGE_SIZE;
            }
            
            var json = JsonSerializer.Serialize(request, options);
            
            AddLog($"→ [{result.ControllerName}] Sending JSON: {json}");
            
            var bytes = Encoding.UTF8.GetBytes(json);

            if (_webSocket?.State == WebSocketState.Open)
            {
                // Create TaskCompletionSource for awaitable response
                var tcs = new TaskCompletionSource<bool>();
                _pendingCompletions[requestId] = tcs;
                
                // Add to pending requests for correlation
                _pendingRequests[requestId] = result;

                try
                {
                    var socket = _webSocket ?? throw new InvalidOperationException("WebSocket is null");
                    await SendAsyncWithLock(socket, bytes, _cts?.Token ?? CancellationToken.None);
                    AddLog($"→ [{result.ControllerName}] Sent read_all via universal stream (RequestId: {requestId})");

                    result.Status = TestStatus.Testing;
                    result.Message = "Request sent via universal stream (awaiting response)";

                    // Wait for response with timeout using TaskCompletionSource
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                    
                    if (completedTask == tcs.Task)
                    {
                        // Check task outcome explicitly
                        if (tcs.Task.IsCanceled)
                        {
                            AddLog($"⚠ [{result.ControllerName}] Request was canceled");
                            result.Status = TestStatus.Failed;
                            result.Message = "Request canceled";
                        }
                        else if (tcs.Task.IsFaulted)
                        {
                            AddLog($"✗ [{result.ControllerName}] Request faulted: {tcs.Task.Exception?.GetBaseException().Message}");
                            result.Status = TestStatus.Failed;
                            result.Message = $"Request failed: {tcs.Task.Exception?.GetBaseException().Message}";
                        }
                        else if (tcs.Task.IsCompletedSuccessfully)
                        {
                            // Response received and processed
                            AddLog($"✓ [{result.ControllerName}] Response received and processed");
                        }
                    }
                    else
                    {
                        // Timeout
                        if (_pendingRequests.TryRemove(requestId, out _))
                        {
                            result.Status = TestStatus.Failed;
                            result.Message = "Timeout waiting for response";
                            AddLog($"✗ [{result.ControllerName}] Timeout waiting for universal stream response (RequestId: {requestId})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"✗ [{result.ControllerName}] SendAsync failed: {ex.Message}");
                    result.Status = TestStatus.Failed;
                    result.Message = $"Send failed: {ex.Message}";
                }
                finally
                {
                    // Cleanup - ensure both dictionaries are cleaned up
                    _pendingCompletions.TryRemove(requestId, out _);
                    _pendingRequests.TryRemove(requestId, out _);
                }
            }
            else
            {
                result.Status = TestStatus.Failed;
                result.Message = $"WebSocket not open (State: {_webSocket?.State})";
                AddLog($"✗ [{result.ControllerName}] WebSocket not open - State: {_webSocket?.State}");
            }
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.Message = $"Error: {ex.Message}";
            AddLog($"✗ [{result.ControllerName}] Test via universal stream failed: {ex.GetType().Name}: {ex.Message}");
            AddLog($"  Stack trace: {ex.StackTrace}");
        }
    }

    private async Task TestControllerSpecificEndpointWithCrud(ControllerTestResult result)
    {
        ClientWebSocket? ws = null;
        CancellationTokenSource? cts = null;
        
        try
        {
            result.Status = TestStatus.Testing;
            result.Message = "Connecting...";

            // Prefer existing AccessToken if non-empty, otherwise load from storage
            string? accessToken = AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                var tokens = await _tokenStorage.GetTokensAsync();
                if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
                {
                    result.Status = TestStatus.Failed;
                    result.Message = "No access token available";
                    AddLog($"✗ {result.ControllerName}: No access token");
                    return;
                }
                accessToken = tokens.AccessToken;
            }
            
            // Normalize token: trim whitespace and remove "Bearer " prefix if present
            accessToken = NormalizeAccessToken(accessToken);
            
            // Validate token is not empty after normalization
            if (string.IsNullOrEmpty(accessToken))
            {
                result.Status = TestStatus.Failed;
                result.Message = "Invalid access token";
                AddLog($"✗ {result.ControllerName}: Access token is empty after normalization");
                return;
            }

            cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("bru.events.v1");
            ws.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            // Secure WebSocket URL construction
            Uri serverUri;
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out serverUri!))
            {
                result.Status = TestStatus.Failed;
                result.Message = "Invalid server URL";
                AddLog($"✗ {result.ControllerName}: Invalid server URL");
                return;
            }

            var buildResult = BuildWebSocketUri(serverUri, result.EndpointPath);
            if (!buildResult.success)
            {
                result.Status = TestStatus.Failed;
                result.Message = buildResult.errorMessage ?? "Invalid URL";
                AddLog($"✗ {result.ControllerName}: {buildResult.errorMessage}");
                return;
            }

            var uri = buildResult.wsUri!;
            AddLog($"→ [{result.ControllerName}] Connecting to {uri}");
            
            await ws.ConnectAsync(uri, cts.Token);
            
            if (ws.State != WebSocketState.Open)
            {
                result.Status = TestStatus.Failed;
                result.Message = $"Connection failed: {ws.State}";
                AddLog($"✗ [{result.ControllerName}] Connection failed: {ws.State}");
                return;
            }

            AddLog($"✓ [{result.ControllerName}] Connected successfully");
            result.Message = "Connected, testing read_all...";

            // Test read_all command with pagination for routeschedules
            string requestId;
            string json;
            if (result.ControllerName == "routeschedules")
            {
                requestId = Guid.NewGuid().ToString();
                var readAllRequest = new
                {
                    command = "read_all",
                    requestId = requestId,
                    page = 1,
                    pageSize = 50
                };
                json = JsonSerializer.Serialize(readAllRequest);
            }
            else
            {
                requestId = Guid.NewGuid().ToString();
                var readAllRequest = new
                {
                    command = "read_all",
                    requestId = requestId
                };
                json = JsonSerializer.Serialize(readAllRequest);
            }

            var bytes = Encoding.UTF8.GetBytes(json);

            AddLog($"→ [{result.ControllerName}] Sending read_all command with requestId: {requestId}");
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);

            // Loop reading frames until we find a matching requestId with type=="result"
            var buffer = new byte[8192];
            string? responseJson = null;
            var maxAttempts = 10;
            var attempts = 0;

            while (attempts < maxAttempts)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await ws.ReceiveAsync(buffer, cts.Token);
                    ms.Write(buffer, 0, receiveResult.Count);
                } while (!receiveResult.EndOfMessage);

                var frameJson = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    using var doc = JsonDocument.Parse(frameJson);
                    var hasMatchingId = doc.RootElement.TryGetProperty("requestId", out var reqIdElement) &&
                                       reqIdElement.GetString() == requestId;
                    var isResultType = doc.RootElement.TryGetProperty("type", out var typeElement) &&
                                      typeElement.GetString() == "result";

                    if (hasMatchingId && isResultType)
                    {
                        responseJson = frameJson;
                        break;
                    }
                    else
                    {
                        AddLog($"← [{result.ControllerName}] Skipping non-matching frame (attempt {attempts + 1})");
                    }
                }
                catch
                {
                    // Ignore parsing errors and continue
                }

                attempts++;
            }

            if (responseJson == null)
            {
                result.Status = TestStatus.Failed;
                result.Message = $"No matching response received after {maxAttempts} attempts";
                AddLog($"✗ [{result.ControllerName}] No matching response received");
                return;
            }

            AddLog($"← [{result.ControllerName}] Response: {responseJson.Substring(0, Math.Min(200, responseJson.Length))}...");

            // Parse response to check success
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var success = false;

                // First check the root `ok` boolean if present
                if (doc.RootElement.TryGetProperty("ok", out var okElement))
                {
                    if (okElement.ValueKind == JsonValueKind.False)
                    {
                        var errorMsg = doc.RootElement.TryGetProperty("error", out var errEl)
                            ? errEl.GetString() ?? "unknown error"
                            : "server returned ok=false";
                        result.Status = TestStatus.Failed;
                        result.Message = $"read_all command failed: {errorMsg}";
                        AddLog($"✗ [{result.ControllerName}] read_all failed (ok=false): {errorMsg}");
                        return;
                    }
                    // ok=true — confirm with operation/property check below
                    success = true;
                }

                if (!success)
                {
                    if (doc.RootElement.TryGetProperty("operation", out var opElement) &&
                        opElement.GetString() == "read_all")
                    {
                        success = true;
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        // Check for expected data property based on controller name
                        var expectedProp = GetExpectedReadAllProperty(result.ControllerName);
                        var hasData = doc.RootElement.EnumerateObject().Any(p => p.Name == expectedProp);
                        success = hasData;
                    }
                }

                if (success)
                {
                    result.Status = TestStatus.Passed;
                    result.Message = "✓ read_all command successful";
                    result.LastTested = DateTime.Now;
                    AddLog($"✓ [{result.ControllerName}] read_all command successful");
                }
                else
                {
                    result.Status = TestStatus.Failed;
                    result.Message = "read_all command failed or returned unexpected format";
                    AddLog($"✗ [{result.ControllerName}] read_all command failed");
                }
            }
            catch (JsonException jsonEx)
            {
                result.Status = TestStatus.Failed;
                result.Message = $"Invalid JSON response: {jsonEx.Message}";
                AddLog($"✗ [{result.ControllerName}] JSON parsing failed: {jsonEx.Message}");
            }

            // Close connection
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", closeCts.Token);
                AddLog($"✓ [{result.ControllerName}] Test complete, connection closed");
            }
            catch (OperationCanceledException)
            {
                AddLog($"⚠ [{result.ControllerName}] Close timeout, aborting connection");
                ws.Abort();
            }
            catch (Exception closeEx)
            {
                AddLog($"⚠ [{result.ControllerName}] Close failed: {closeEx.Message}, aborting");
                ws.Abort();
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = TestStatus.Failed;
            result.Message = "Test timeout (30s)";
            AddLog($"✗ [{result.ControllerName}] Test timeout");
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.Message = $"Error: {ex.Message}";
            AddLog($"✗ [{result.ControllerName}] Error: {ex.Message}");
        }
        finally
        {
            try
            {
                // Close connection with timeout + Abort fallback
                if (ws?.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cleanup", closeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - fall back to abort
                        ws.Abort();
                    }
                    catch
                    {
                        // Any other error - fall back to abort
                        ws.Abort();
                    }
                }
            }
            catch { /* Ignore cleanup errors */ }
            
            ws?.Dispose();
            cts?.Dispose();
        }
    }

    private async Task SendAsyncWithLock(WebSocket socket, byte[] bytes, CancellationToken cancellationToken)
    {
        await SendAsyncWithLock(socket, bytes, _sendSemaphore, cancellationToken);
    }

    private static async Task SendAsyncWithLock(WebSocket socket, byte[] bytes, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        if (socket == null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"WebSocket is not open (State: {socket?.State})");
        }

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check state after acquiring semaphore
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"WebSocket state changed to {socket.State} after acquiring lock");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }


    [RelayCommand]
    private async Task SendBroadcastTest()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected. Please connect first.";
            return;
        }

        try
        {
            // Use the authenticated API client
            var httpClient = _apiClient.CreateClient();
            
            AddLog("Sending broadcast test...");
            var response = await httpClient.PostAsync("realtime/broadcast-test", null);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                AddLog($"✓ Broadcast test sent: {content}");
                StatusMessage = "Broadcast test sent";
            }
            else
            {
                AddLog($"✗ Broadcast test failed: {response.StatusCode} - {content}");
                StatusMessage = $"Broadcast test failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Broadcast test error: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendPing()
    {
        if (!IsConnected || _webSocket?.State != WebSocketState.Open)
        {
            StatusMessage = "Not connected. Please connect first.";
            AddLog("⚠ Cannot send ping - not connected to universal stream");
            return;
        }

        try
        {
            var ping = new
            {
                command = "ping",
                requestId = Guid.NewGuid().ToString(),
                payload = new
                {
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            var json = JsonSerializer.Serialize(ping, options);
            AddLog($"🏓 Sending ping JSON: {json}");

            var bytes = Encoding.UTF8.GetBytes(json);

            var socket = _webSocket ?? throw new InvalidOperationException("WebSocket is null");
            await SendAsyncWithLock(socket, bytes, _cts?.Token ?? CancellationToken.None);
            AddLog("🏓 Ping sent to universal stream");
        }
        catch (Exception ex)
        {
            AddLog($"✗ Ping error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SubscribeToResource(string resource)
    {
        if (!IsConnected || _webSocket?.State != WebSocketState.Open)
        {
            StatusMessage = "Not connected. Please connect first.";
            AddLog("⚠ Cannot subscribe - not connected to universal stream");
            return;
        }

        try
        {
            var subscribe = new
            {
                command = "subscribe",
                resource = resource,
                requestId = Guid.NewGuid().ToString()
            };

            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            var json = JsonSerializer.Serialize(subscribe, options);
            AddLog($"📢 Sending subscribe JSON: {json}");
            
            var bytes = Encoding.UTF8.GetBytes(json);
            
            var socket = _webSocket ?? throw new InvalidOperationException("WebSocket is null");
            await SendAsyncWithLock(socket, bytes, _cts?.Token ?? CancellationToken.None);
            AddLog($"📢 Subscribe request sent for resource: {resource}");
        }
        catch (Exception ex)
        {
            AddLog($"✗ Subscribe error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UnsubscribeFromResource(string resource)
    {
        if (!IsConnected || _webSocket?.State != WebSocketState.Open)
        {
            StatusMessage = "Not connected. Please connect first.";
            AddLog("⚠ Cannot unsubscribe - not connected to universal stream");
            return;
        }

        try
        {
            var unsubscribe = new
            {
                command = "unsubscribe",
                resource = resource,
                requestId = Guid.NewGuid().ToString()
            };

            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            var json = JsonSerializer.Serialize(unsubscribe, options);
            AddLog($"📢 Sending unsubscribe JSON: {json}");
            
            var bytes = Encoding.UTF8.GetBytes(json);
            
            var socket = _webSocket ?? throw new InvalidOperationException("WebSocket is null");
            await SendAsyncWithLock(socket, bytes, _cts?.Token ?? CancellationToken.None);
            AddLog($"📢 Unsubscribe request sent for resource: {resource}");
        }
        catch (Exception ex)
        {
            AddLog($"✗ Unsubscribe error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        EventLog.Clear();
        StatusMessage = "Log cleared";
        Log.Information("[WebSocketDebug] Log cleared by user");
    }

    [RelayCommand]
    private void ResetTests()
    {
        InitializeTestResults();
        AddLog("Tests reset");
        StatusMessage = "Tests reset";
    }

    /// <summary>
    /// Ensures a persistent connection to /api/realtime/interactive exists and returns it.
    /// Creates a new connection if none exists or the existing one is closed.
    /// </summary>
    private async Task<ClientWebSocket?> GetOrConnectInteractiveSocketAsync()
    {
        // Serialize concurrent callers so only one connect/dispose cycle runs at a time.
        await _interactiveLock.WaitAsync();
        try
        {
        if (_interactiveWebSocket?.State == WebSocketState.Open)
            return _interactiveWebSocket;

        // Dispose stale socket
        _interactiveCts?.Cancel();
        _interactiveCts?.Dispose();
        _interactiveWebSocket?.Dispose();
        IsInteractiveConnected = false;

        string? accessToken = AccessToken;
        if (string.IsNullOrEmpty(accessToken))
        {
            var tokens = await _tokenStorage.GetTokensAsync();
            accessToken = tokens?.AccessToken;
        }
        if (string.IsNullOrEmpty(accessToken))
        {
            AddLog("❌ No access token available for interactive socket.");
            return null;
        }
        accessToken = NormalizeAccessToken(accessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            AddLog("❌ Normalized access token is empty. Aborting interactive socket connection.");
            return null;
        }

        Uri serverUri;
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out serverUri!))
        {
            AddLog("❌ Invalid server URL for interactive socket.");
            return null;
        }

        var buildResult = BuildWebSocketUri(serverUri, "/api/realtime/interactive");
        if (!buildResult.success)
        {
            AddLog($"❌ Cannot build interactive WebSocket URI: {buildResult.errorMessage}");
            return null;
        }

        _interactiveCts = new CancellationTokenSource();
        _interactiveWebSocket = new ClientWebSocket();
        _interactiveWebSocket.Options.AddSubProtocol("bru.interactive.v1");
        _interactiveWebSocket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");

        using var interactiveTimeoutCts = new CancellationTokenSource(HandshakeTimeout);
        using var interactiveLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(_interactiveCts.Token, interactiveTimeoutCts.Token);
        await _interactiveWebSocket.ConnectAsync(buildResult.wsUri!, interactiveLinkedCts.Token);
        AddLog($"✅ Connected to interactive endpoint: {buildResult.wsUri}");
        IsInteractiveConnected = true;

        // Start a single background receive loop that reads all frames and dispatches
        // them to the matching pending TCS, or logs unmatched frames.
        var loopCts = _interactiveCts;
        var loopWs  = _interactiveWebSocket;
        _ = Task.Run(async () =>
        {
            var buf = new byte[8192];
            try
            {
                while (!loopCts.Token.IsCancellationRequested && loopWs.State == WebSocketState.Open)
                {
                    // Accumulate fragments until EndOfMessage to handle fragmented frames.
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    try
                    {
                        do
                        {
                            result = await loopWs.ReceiveAsync(buf, loopCts.Token);
                            ms.Write(buf, 0, result.Count);
                        } while (!result.EndOfMessage);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { break; }

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var frame = Encoding.UTF8.GetString(ms.ToArray());

                    // Try to extract requestId and dispatch to the waiting TCS.
                    string? rid = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(frame);
                        if (doc.RootElement.TryGetProperty("requestId", out var ridEl))
                            rid = ridEl.GetString();
                    }
                    catch { /* non-JSON frame */ }

                    if (rid != null && _interactivePending.TryRemove(rid, out var tcs))
                    {
                        tcs.TrySetResult(frame);
                    }
                    else
                    {
                        // Welcome frame or unsolicited push — just log it.
                        AddLog($"📨 Server push: {frame}");
                    }
                }
            }
            finally
            {
                // Only clear state if this loop still owns the connection
                // (prevents old loop from clobbering state after reconnect)
                if (ReferenceEquals(_interactiveCts, loopCts) && ReferenceEquals(_interactiveWebSocket, loopWs))
                {
                    // Cancel all pending requests when the loop exits.
                    foreach (var kv in _interactivePending)
                        kv.Value.TrySetCanceled();
                    _interactivePending.Clear();
                    _activeStreamIds.Clear();
                    IsInteractiveConnected = false;
                    OnPropertyChanged(nameof(HasActiveStreams));
                }
            }
        });

        return _interactiveWebSocket;
        } // end try
        finally
        {
            _interactiveLock.Release();
        }
    }

    [RelayCommand]
    private async Task SendInteractiveCommand(string? commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
        {
            AddLog("❌ Command type is required.");
            return;
        }

        try
        {
            var ws = await GetOrConnectInteractiveSocketAsync();
            if (ws == null || ws.State != WebSocketState.Open)
            {
                AddLog("❌ Could not connect to interactive endpoint.");
                return;
            }

            var requestId = Guid.NewGuid().ToString("N")[..8];
            object message = commandType.ToLowerInvariant() switch
            {
                "echo" => new { command = "echo", requestId, data = $"Test from Avalonia" },
                "time" => new { command = "time", requestId },
                "stats" => new { command = "stats", requestId },
                "ping" => new { command = "ping", requestId },
                "help" => new { command = "help", requestId },
                "calculate" => new { command = "calculate", requestId, expression = "2+2*3" },
                "stream:buses" => new { command = "stream:start", requestId, resource = "buses" },
                "stream:routes" => new { command = "stream:start", requestId, resource = "routes" },
                _ => (object)new { command = commandType, requestId }
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, options));

            // Register a TCS for this requestId BEFORE sending to avoid race conditions
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _interactivePending[requestId] = tcs;

            try
            {
                await SendAsyncWithLock(ws, bytes, _interactiveSendLock, _interactiveCts!.Token);
            }
            catch
            {
                // If send fails, clean up the TCS so the background loop doesn't leak it.
                _interactivePending.TryRemove(requestId, out _);
                throw;
            }
            AddLog($"📤 Sent {commandType} command (ID: {requestId})");

            _ = Task.Run(async () =>
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_interactiveCts.Token);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    var response = await tcs.Task.WaitAsync(timeoutCts.Token);
                    AddLog($"📨 {commandType} response: {response}");

                    // Track stream IDs so the UI can stop them later.
                    if (commandType.StartsWith("stream:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(response);
                            if (doc.RootElement.TryGetProperty("streamId", out var sidEl))
                            {
                                var sid = sidEl.GetString();
                                if (!string.IsNullOrEmpty(sid))
                                {
                                    var resource = doc.RootElement.TryGetProperty("resource", out var resEl)
                                        ? resEl.GetString() ?? commandType : commandType;
                                    _activeStreamIds[sid] = resource;
                                    OnPropertyChanged(nameof(HasActiveStreams));
                                    AddLog($"▶ Stream started: {sid} ({resource})");
                                }
                            }
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
                catch (OperationCanceledException)
                {
                    AddLog($"⚠ {commandType} response timed out");
                }
                catch (Exception ex)
                {
                    AddLog($"⚠ {commandType} receive error: {ex.Message}");
                }
                finally
                {
                    // Ensure requestId is removed from _interactivePending in all paths
                    _interactivePending.TryRemove(requestId, out _);
                }
            });
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error sending {commandType} command: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopStream(string? streamId)
    {
        if (string.IsNullOrWhiteSpace(streamId))
        {
            AddLog("❌ Stream ID is required.");
            return;
        }

        try
        {
            var ws = await GetOrConnectInteractiveSocketAsync();
            if (ws == null || ws.State != WebSocketState.Open)
            {
                AddLog("❌ Not connected to interactive endpoint.");
                return;
            }

            var requestId = Guid.NewGuid().ToString("N")[..8];
            var message = new { command = "stream:stop", requestId, streamId };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            await SendAsyncWithLock(ws, bytes, _interactiveSendLock, _interactiveCts!.Token);
            _activeStreamIds.TryRemove(streamId, out _);
            OnPropertyChanged(nameof(HasActiveStreams));
            AddLog($"⏹ Sent stream:stop for {streamId}");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error stopping stream: {ex.Message}");
        }
    }

    /// <summary>True when at least one interactive stream is active.</summary>
    public bool HasActiveStreams => !_activeStreamIds.IsEmpty;

    [RelayCommand]
    private async Task StopAllStreams()
    {
        if (_activeStreamIds.IsEmpty)
        {
            AddLog("⚠ No active streams to stop.");
            return;
        }

        var ids = _activeStreamIds.Keys.ToList();
        foreach (var sid in ids)
            await StopStream(sid);

        AddLog($"⏹ Stopped {ids.Count} stream(s).");
    }

    [RelayCommand]
    private async Task TestInteractiveEndpoint()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            AddLog("❌ Server URL is required");
            return;
        }

        try
        {
            AddLog("🧪 Testing interactive WebSocket endpoint...");
            
            var serverUri = new Uri(ServerUrl);
            var (success, wsUri, errorMessage) = BuildWebSocketUri(serverUri, "/api/realtime/interactive");

            if (!success || wsUri == null)
            {
                AddLog($"❌ Failed to build WebSocket URI: {errorMessage}");
                return;
            }

            using var testSocket = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            testSocket.Options.AddSubProtocol("bru.interactive.v1");

            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                var normalizedToken = NormalizeAccessToken(AccessToken);
                if (!string.IsNullOrWhiteSpace(normalizedToken))
                {
                    testSocket.Options.SetRequestHeader("Authorization", $"Bearer {normalizedToken}");
                }
            }

            await testSocket.ConnectAsync(wsUri, cts.Token);
            AddLog($"✅ Connected to interactive endpoint: {wsUri}");

            // Receive welcome message
            var buffer = new byte[8192];
            using (var ms = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await testSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var welcomeMsg = Encoding.UTF8.GetString(ms.ToArray());
                AddLog($"📨 Welcome: {welcomeMsg}");
            }

            // Test echo command
            var echoCmd = new { command = "echo", requestId = "test-1", data = "Test from Avalonia" };
            var echoJson = JsonSerializer.Serialize(echoCmd);
            await testSocket.SendAsync(Encoding.UTF8.GetBytes(echoJson), WebSocketMessageType.Text, true, cts.Token);
            AddLog($"📤 Sent echo command");

            using (var ms = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await testSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var echoResponse = Encoding.UTF8.GetString(ms.ToArray());
                AddLog($"📨 Echo response: {echoResponse}");
            }

            // Test time command
            var timeCmd = new { command = "time", requestId = "test-2" };
            var timeJson = JsonSerializer.Serialize(timeCmd);
            await testSocket.SendAsync(Encoding.UTF8.GetBytes(timeJson), WebSocketMessageType.Text, true, cts.Token);
            AddLog($"📤 Sent time command");

            using (var ms = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await testSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var timeResponse = Encoding.UTF8.GetString(ms.ToArray());
                AddLog($"📨 Time response: {timeResponse}");
            }

            await testSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", cts.Token);
            AddLog("✅ Interactive endpoint test completed successfully");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Interactive endpoint test failed: {ex.Message}");
        }
    }

    private async Task ReceiveMessagesAsync(ClientWebSocket? webSocket, CancellationTokenSource? cts)
    {
        // Use captured locals instead of instance fields to prevent old tasks from interfering with new connections
        if (webSocket == null || cts == null)
        {
            AddLog("✗ ReceiveMessagesAsync: webSocket or cts is null");
            return;
        }

        var buffer = new byte[8192];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, cts.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    AddLog("Server closed connection");
                    await CleanupWebSocketAsync();
                    break;
                }

                var json = Encoding.UTF8.GetString(ms.ToArray());
                AddLog($"← Received RAW: {json}");

                // Parse and update test results if it's a response
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    AddLog($"← JSON parsed successfully, root type: {doc.RootElement.ValueKind}");
                    
                    if (doc.RootElement.TryGetProperty("type", out var typeElement))
                    {
                        var type = typeElement.GetString();
                        AddLog($"← Message type: {type}");
                        
                        if (type == "event")
                        {
                            if (doc.RootElement.TryGetProperty("eventName", out var eventName))
                            {
                                AddLog($"📡 Event: {eventName.GetString()}");
                            }
                        }
                        else if (type == "result")
                        {
                            // Handle CRUD result from universal stream
                            var ok = doc.RootElement.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
                            var command = doc.RootElement.TryGetProperty("command", out var cmdElement) ? cmdElement.GetString() : "unknown";
                            var resource = doc.RootElement.TryGetProperty("resource", out var resElement) ? resElement.GetString() : "unknown";
                            
                            AddLog($"📬 Result parsed - Command: {command}, Resource: {resource}, OK: {ok}");
                            
                            // Correlate response with pending request
                            if (doc.RootElement.TryGetProperty("requestId", out var requestIdElement))
                            {
                                var requestId = requestIdElement.GetString();
                                AddLog($"📬 RequestId from response: {requestId}");
                                AddLog($"📬 Pending requests count: {_pendingRequests.Count}");
                                
                                if (!string.IsNullOrEmpty(requestId) && _pendingRequests.TryRemove(requestId, out var testResult))
                                {
                                    AddLog($"📬 Found matching pending request for {testResult.ControllerName}");

                                    // Prefer context from the pending request when parsed values are unknown
                                    var resolvedCommand = (command == "unknown" || string.IsNullOrEmpty(command)) ? testResult.Command : command;
                                    var resolvedResource = (resource == "unknown" || string.IsNullOrEmpty(resource)) ? (testResult.Resource ?? testResult.ControllerName) : resource;

                                    // Extract error detail from response JSON if present
                                    string? errorDetail = null;
                                    if (!ok && doc.RootElement.TryGetProperty("error", out var errEl))
                                        errorDetail = errEl.GetString();

                                    // Update observable properties on UI thread
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        testResult.Status = ok ? TestStatus.Passed : TestStatus.Failed;
                                        testResult.Message = ok
                                            ? $"✓ {resolvedCommand} via universal stream successful"
                                            : $"✗ {resolvedCommand} via universal stream failed{(errorDetail != null ? $": {errorDetail}" : string.Empty)}";
                                        testResult.LastTested = DateTime.Now;
                                        AddLog($"📬 Updated test result for {testResult.ControllerName}: {testResult.Status}");
                                    });
                                    
                                    // Signal completion
                                    if (_pendingCompletions.TryGetValue(requestId, out var tcs))
                                    {
                                        tcs.TrySetResult(ok);
                                    }
                                }
                                else
                                {
                                    AddLog($"⚠ No matching pending request found for RequestId: {requestId}");
                                }
                            }
                            else
                            {
                                AddLog($"⚠ Result message has no requestId property");
                            }
                        }
                        else if (type == "error")
                        {
                            // Handle error response
                            var message = doc.RootElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown error";
                            AddLog($"❌ Error message: {message}");
                            
                            // Correlate with pending request if requestId present
                            if (doc.RootElement.TryGetProperty("requestId", out var requestIdElement))
                            {
                                var requestId = requestIdElement.GetString();
                                AddLog($"❌ Error RequestId: {requestId}");
                                
                                if (!string.IsNullOrEmpty(requestId) && _pendingRequests.TryRemove(requestId, out var testResult))
                                {
                                    AddLog($"❌ Found matching pending request for error");
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        testResult.Status = TestStatus.Failed;
                                        testResult.Message = $"Error: {message}";
                                        testResult.LastTested = DateTime.Now;
                                    });
                                    
                                    // Signal completion with error
                                    if (_pendingCompletions.TryGetValue(requestId, out var tcs))
                                    {
                                        tcs.TrySetResult(false);
                                    }
                                }
                                else
                                {
                                    AddLog($"⚠ No matching pending request for error RequestId: {requestId}");
                                }
                            }
                            else
                            {
                                AddLog($"⚠ Error message has no requestId");
                            }
                        }
                        else if (type == "pong")
                        {
                            AddLog($"🏓 Pong received");
                        }
                        else if (type == "subscribed" || type == "unsubscribed")
                        {
                            var resource = doc.RootElement.TryGetProperty("resource", out var resElement) ? resElement.GetString() : "unknown";
                            AddLog($"📢 {type}: {resource}");
                        }
                        else
                        {
                            AddLog($"⚠ Unknown message type: {type}");
                        }
                    }
                    else
                    {
                        AddLog($"⚠ Message has no 'type' property. Properties: {string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name))}");
                    }
                }
                catch (JsonException ex)
                {
                    AddLog($"⚠ JSON parse error: {ex.GetType().Name}: {ex.Message}");
                    AddLog($"  Failed JSON: {json}");
                }
                catch (Exception ex)
                {
                    AddLog($"⚠ Parse error: {ex.GetType().Name}: {ex.Message}");
                    AddLog($"  Stack: {ex.StackTrace}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("Receive operation cancelled");
            await CleanupWebSocketAsync();
        }
        catch (Exception ex)
        {
            AddLog($"✗ Receive error: {ex.Message}");
            await CleanupWebSocketAsync();
        }
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        
        // Log to Serilog based on message prefix
        if (message.StartsWith("✗") || message.StartsWith("❌"))
        {
            Log.Error("[WebSocketDebug] {Message}", message);
        }
        else if (message.StartsWith("⚠"))
        {
            Log.Warning("[WebSocketDebug] {Message}", message);
        }
        else if (message.StartsWith("✓") || message.StartsWith("📬") || message.StartsWith("📡"))
        {
            Log.Information("[WebSocketDebug] {Message}", message);
        }
        else if (message.StartsWith("→") || message.StartsWith("←"))
        {
            Log.Debug("[WebSocketDebug] {Message}", message);
        }
        else
        {
            Log.Information("[WebSocketDebug] {Message}", message);
        }
        
        Dispatcher.UIThread.Post(() =>
        {
            EventLog.Add($"[{timestamp}] {message}");
            TrimEventLog();
        });
    }
    
    private void TrimEventLog()
    {
        // Keep log size manageable - remove oldest entries in bulk
        const int maxLogSize = 500;
        if (EventLog.Count > maxLogSize)
        {
            var itemsToRemove = EventLog.Count - maxLogSize;
            // Remove in bulk by creating new collection from remaining items to avoid O(n²)
            var remaining = EventLog.Skip(itemsToRemove).ToList();
            EventLog.Clear();
            foreach (var item in remaining)
            {
                EventLog.Add(item);
            }
        }
    }
    
    /// <summary>
    /// Returns the expected collection property name for a controller's read_all response.
    /// </summary>
    private static string GetExpectedReadAllProperty(string controllerName)
    {
        return controllerName.ToLowerInvariant() switch
        {
            "buses" => "buses",
            "employees" => "employees",
            "jobs" => "jobs",
            "maintenance" => "records",
            "permissions" => "permissions",
            "roles" => "roles",
            "routes" => "routes",
            "routeschedules" => "schedules",
            "tickets" => "tickets",
            "ticketsales" => "sales",
            "users" => "users",
            _ => controllerName // Default to controller name
        };
    }
    
    public void Dispose()
    {
        // Use fire-and-forget with ConfigureAwait(false) to avoid deadlocking the UI thread
        Task.Run(async () => await CleanupWebSocketAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

        _interactiveCts?.Cancel();
        _interactiveCts?.Dispose();
        _interactiveWebSocket?.Dispose();
        IsInteractiveConnected = false;
        _interactiveSendLock?.Dispose();
        _interactiveLock?.Dispose();
        foreach (var kv in _interactivePending) kv.Value.TrySetCanceled();
        _interactivePending.Clear();

        _sendSemaphore?.Dispose();
        _cts?.Dispose();
        _webSocket?.Dispose();
        _lifecycleLock?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupWebSocketAsync().ConfigureAwait(false);

        _interactiveCts?.Cancel();
        _interactiveCts?.Dispose();
        _interactiveWebSocket?.Dispose();
        IsInteractiveConnected = false;
        _interactiveSendLock?.Dispose();
        _interactiveLock?.Dispose();
        foreach (var kv in _interactivePending) kv.Value.TrySetCanceled();
        _interactivePending.Clear();

        _sendSemaphore?.Dispose();
        _cts?.Dispose();
        _webSocket?.Dispose();
        _lifecycleLock?.Dispose();
    }
}

public partial class ControllerTestResult : ObservableObject
{
    [ObservableProperty]
    private string _controllerName = "";

    [ObservableProperty]
    private string _endpointPath = "";

    [ObservableProperty]
    private TestStatus _status = TestStatus.NotTested;

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private DateTime? _lastTested;

    /// <summary>The CRUD command sent with this request (e.g. "read_all", "create").</summary>
    public string? Command { get; set; }

    /// <summary>The resource/controller name from the request context.</summary>
    public string? Resource { get; set; }
}

public enum TestStatus
{
    NotTested,
    Testing,
    Passed,
    Failed
}