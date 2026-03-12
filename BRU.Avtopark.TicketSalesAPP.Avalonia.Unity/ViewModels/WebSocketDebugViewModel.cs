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

public partial class WebSocketDebugViewModel : ObservableObject
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
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private ObservableCollection<ControllerTestResult> _testResults = new();

    [ObservableProperty]
    private ObservableCollection<string> _eventLog = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ControllerTestResult> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingCompletions = new();

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
        _ = LoadAuthenticationData();
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

            // Ensure we have a valid token
            var tokens = await _tokenStorage.GetTokensAsync();
            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                StatusMessage = "Authentication required";
                AddLog("✗ No access token available. Please login first.");
                return;
            }

            AccessToken = tokens.AccessToken;

            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.AddSubProtocol("bru.events.v1");
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {tokens.AccessToken}");

            var wsUrl = ServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            var uri = new Uri($"{wsUrl}/api/realtime/stream");

            AddLog($"Connecting to {uri}...");
            await _webSocket.ConnectAsync(uri, _cts.Token);
            IsConnected = true;
            StatusMessage = "Connected";
            AddLog($"✓ Connected successfully");

            // Start receiving messages with explicit exception handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReceiveMessagesAsync();
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
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectWebSocket()
    {
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnect", CancellationToken.None);
            }

            _cts?.Cancel();
            _webSocket?.Dispose();
            _cts?.Dispose();

            IsConnected = false;
            StatusMessage = "Disconnected";
            AddLog("Disconnected from WebSocket");
        }
        catch (Exception ex)
        {
            AddLog($"Error during disconnect: {ex.Message}");
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

    private async Task TestController(ControllerTestResult result)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var request = new
            {
                command = "read_all",
                requestId = requestId,
                resource = result.ControllerName
            };

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);

            if (_webSocket?.State == WebSocketState.Open)
            {
                // Add to pending requests for correlation
                _pendingRequests[requestId] = result;

                await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
                AddLog($"→ Sent test request for {result.ControllerName} via universal stream");

                result.Status = TestStatus.Testing;
                result.Message = "Request sent (awaiting response)";
            }
            else
            {
                result.Status = TestStatus.Failed;
                result.Message = "WebSocket not open";
            }
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.Message = $"Error: {ex.Message}";
            AddLog($"✗ Test failed for {result.ControllerName}: {ex.Message}");
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

                await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
                AddLog($"→ [{result.ControllerName}] Sent read_all via universal stream (RequestId: {requestId})");

                result.Status = TestStatus.Testing;
                result.Message = "Request sent via universal stream (awaiting response)";

                // Wait for response with timeout using TaskCompletionSource
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                
                if (completedTask == tcs.Task)
                {
                    // Response received and processed
                    AddLog($"✓ [{result.ControllerName}] Response received and processed");
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
                
                // Cleanup
                _pendingCompletions.TryRemove(requestId, out _);
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

            var tokens = await _tokenStorage.GetTokensAsync();
            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                result.Status = TestStatus.Failed;
                result.Message = "No access token available";
                AddLog($"✗ {result.ControllerName}: No access token");
                return;
            }

            cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("bru.events.v1");
            ws.Options.SetRequestHeader("Authorization", $"Bearer {tokens.AccessToken}");

            var wsUrl = ServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            var uri = new Uri($"{wsUrl}{result.EndpointPath}");

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
            string json;
            if (result.ControllerName == "routeschedules")
            {
                var readAllRequest = new
                {
                    command = "read_all",
                    requestId = Guid.NewGuid().ToString(),
                    payload = new { page = 1, pageSize = 50 }
                };
                json = JsonSerializer.Serialize(readAllRequest);
            }
            else
            {
                var readAllRequest = new
                {
                    command = "read_all",
                    requestId = Guid.NewGuid().ToString()
                };
                json = JsonSerializer.Serialize(readAllRequest);
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            
            AddLog($"→ [{result.ControllerName}] Sending read_all command");
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);

            // Receive response
            var buffer = new byte[8192];
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult receiveResult;

            do
            {
                receiveResult = await ws.ReceiveAsync(buffer, cts.Token);
                ms.Write(buffer, 0, receiveResult.Count);
            } while (!receiveResult.EndOfMessage);

            var responseJson = Encoding.UTF8.GetString(ms.ToArray());
            AddLog($"← [{result.ControllerName}] Response: {responseJson.Substring(0, Math.Min(200, responseJson.Length))}...");

            // Parse response to check success
            var doc = JsonDocument.Parse(responseJson);
            var success = false;
            
            if (doc.RootElement.TryGetProperty("operation", out var opElement) && 
                opElement.GetString() == "read_all")
            {
                success = true;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Check for data properties that indicate successful read_all
                var hasData = doc.RootElement.EnumerateObject().Any(p => 
                    p.Name.EndsWith("s") || // buses, employees, etc.
                    p.Name == "schedules" ||
                    p.Name == "sales" ||
                    p.Name == "records");
                success = hasData;
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

            // Close connection
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            AddLog($"✓ [{result.ControllerName}] Test complete, connection closed");
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
                if (ws?.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cleanup", CancellationToken.None);
                }
            }
            catch { /* Ignore cleanup errors */ }
            
            ws?.Dispose();
            cts?.Dispose();
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
            var response = await httpClient.PostAsync("api/realtime/broadcast-test", null);
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
                type = "ping",
                requestId = Guid.NewGuid().ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            var json = JsonSerializer.Serialize(ping, options);
            AddLog($"🏓 Sending ping JSON: {json}");
            
            var bytes = Encoding.UTF8.GetBytes(json);
            
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
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
            
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
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
            
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
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
        AddLog("Log cleared");
    }

    [RelayCommand]
    private void ResetTests()
    {
        InitializeTestResults();
        AddLog("Tests reset");
        StatusMessage = "Tests reset";
    }

    private async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[8192];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !(_cts?.Token.IsCancellationRequested ?? true))
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    AddLog("Server closed connection");
                    IsConnected = false;
                    StatusMessage = "Connection closed by server";
                    break;
                }

                var json = Encoding.UTF8.GetString(ms.ToArray());
                AddLog($"← Received RAW: {json}");

                // Parse and update test results if it's a response
                try
                {
                    var doc = JsonDocument.Parse(json);
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
                                    
                                    // Update observable properties on UI thread
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        testResult.Status = ok ? TestStatus.Passed : TestStatus.Failed;
                                        testResult.Message = ok ? $"✓ {command} via universal stream successful" : $"✗ {command} via universal stream failed";
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
        }
        catch (Exception ex)
        {
            AddLog($"✗ Receive error: {ex.Message}");
            IsConnected = false;
            StatusMessage = $"Error: {ex.Message}";
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
        if (EventLog.Count > 500)
        {
            var keepCount = 500;
            var itemsToKeep = EventLog.Skip(EventLog.Count - keepCount).ToList();
            
            EventLog.Clear();
            foreach (var item in itemsToKeep)
            {
                EventLog.Add(item);
            }
        }
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
}

public enum TestStatus
{
    NotTested,
    Testing,
    Passed,
    Failed
}
