using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{ // Services/Implementations/AdminActionLogger.cs
public class AdminActionLogger : IAdminActionLogger
{
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ILogger<AdminActionLogger> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminActionLogger(
        ISpacetimeDBService spacetimeService,
        ILogger<AdminActionLogger> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public async Task LogActionAsync(string userId, string action, string details)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(action))
                throw new ArgumentNullException(nameof(action));

            var conn = _spacetimeService.GetConnection();
            var httpContext = _httpContextAccessor.HttpContext;
            
            // Get the next log ID
            uint logId = 0;
            var counter = conn.Db.LogIdCounter.Key.Find("logId");
            if (counter == null)
            {
                // Create new counter
                conn.Reducers.LogAdminAction(
                    userId,
                    "CreateCounter",
                    "Created admin log counter",
                    DateTimeOffset.UtcNow.ToString("o"),
                    httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                    httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown"
                );
                logId = 1;
            }
            else
            {
                counter.NextId++;
                logId = counter.NextId;
            }
            
            // Create the log entry
            conn.Reducers.LogAdminAction(
                userId,
                action,
                details ?? string.Empty,
                DateTimeOffset.UtcNow.ToString("o"),
                httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown"
            );

            _logger.LogInformation(
                "Admin action logged: {Action} by user {UserId} from {IpAddress}",
                action, userId, httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown");
            
            await Task.CompletedTask; // To maintain async signature
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging admin action for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<AdminActionLog>> GetUserActionsAsync(
        string userId, 
        DateTime? startDate = null, 
        DateTime? endDate = null)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(nameof(userId));

            var conn = _spacetimeService.GetConnection();
            
            // Convert dates to Unix timestamps
            ulong? startTimestamp = startDate.HasValue 
                ? (ulong)new DateTimeOffset(startDate.Value).ToUnixTimeMilliseconds() 
                : null;
                
            ulong? endTimestamp = endDate.HasValue 
                ? (ulong)new DateTimeOffset(endDate.Value).ToUnixTimeMilliseconds() 
                : null;
            
            // Query logs
            var logs = conn.Db.AdminActionLog.Iter()
                .Where(l => l.UserId.ToString() == userId)
                .ToList();
                
            if (startTimestamp.HasValue)
                logs = logs.Where(l => l.Timestamp >= startTimestamp.Value).ToList();
                
            if (endTimestamp.HasValue)
                logs = logs.Where(l => l.Timestamp <= endTimestamp.Value).ToList();
            
            // Return logs ordered by timestamp descending
            return logs.OrderByDescending(l => l.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user actions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<AdminActionLog>> GetActionsByTypeAsync(
        string actionType, 
        DateTime? startDate = null, 
        DateTime? endDate = null)
    {
        try
        {
            if (string.IsNullOrEmpty(actionType))
                throw new ArgumentNullException(nameof(actionType));

            var conn = _spacetimeService.GetConnection();
            
            // Convert dates to Unix timestamps
            ulong? startTimestamp = startDate.HasValue 
                ? (ulong)new DateTimeOffset(startDate.Value).ToUnixTimeMilliseconds() 
                : null;
                
            ulong? endTimestamp = endDate.HasValue 
                ? (ulong)new DateTimeOffset(endDate.Value).ToUnixTimeMilliseconds() 
                : null;
            
            // Query logs
            var logs = conn.Db.AdminActionLog.Iter()
                .Where(l => l.Action == actionType)
                .ToList();
                
            if (startTimestamp.HasValue)
                logs = logs.Where(l => l.Timestamp >= startTimestamp.Value).ToList();
                
            if (endTimestamp.HasValue)
                logs = logs.Where(l => l.Timestamp <= endTimestamp.Value).ToList();
            
            // Return logs ordered by timestamp descending
            return logs.OrderByDescending(l => l.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving actions by type {ActionType}", actionType);
            throw;
        }
    }
}}