using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using SpacetimeDB;

internal static class CatalogReadAssertions
{
    internal static async Task MatchesListAsync(ApplicationOperations operations, Identity actor, string resource, uint id, ulong revision, CancellationToken ct = default)
    {
        var current = await operations.ExecuteAsync(resource + ".get", actor, JsonSerializer.SerializeToElement(new EntityRequest(id), ApiProtocol.Json), null, ct);
        var expectedType = resource switch
        {
            "routes" => typeof(RouteRow), "employees" => typeof(EmployeeRow), "maintenance" => typeof(MaintenanceRow), "tickets" => typeof(TicketRow),
            "schedules" => typeof(ScheduleRow), "roles" => typeof(RoleRow), "permissions" => typeof(PermissionRow), "users" => typeof(UserRow), _ => throw new Exception(resource)
        };
        var currentJson = JsonSerializer.SerializeToElement(current, ApiProtocol.Json);
        if (current?.GetType() != expectedType || currentJson.GetProperty("revision").GetUInt64() != revision) throw new Exception(resource + " get returned incorrect typed row/revision");
        var key = resource switch { "maintenance" => "maintenanceId", "employees" => "employeeId", "routes" => "routeId", _ => "id" };
        for (var page = 1; ; page++)
        {
            var result = await operations.ExecuteAsync(resource + ".list", actor, JsonSerializer.SerializeToElement(new PageRequest(page, 100), ApiProtocol.Json), null, ct);
            var json = JsonSerializer.SerializeToElement(result, ApiProtocol.Json);
            foreach (var row in json.GetProperty("items").EnumerateArray())
                if (row.GetProperty(key).GetUInt32() == id)
                {
                    if (!JsonElement.DeepEquals(row, currentJson)) throw new Exception(resource + " current-row projection differs from list row");
                    return;
                }
            if ((long)page * 100 >= json.GetProperty("totalCount").GetInt32()) throw new Exception(resource + " get returned an entity missing from its list");
        }
    }

    internal static async Task MissingAsync(ApplicationOperations operations, Identity actor, string resource, uint id, CancellationToken ct = default)
    {
        try { await operations.ExecuteAsync(resource + ".get", actor, JsonSerializer.SerializeToElement(new EntityRequest(id), ApiProtocol.Json), null, ct); }
        catch (OperationException ex) when (ex.Code == "not_found") { return; }
        throw new Exception(resource + " deleted entity did not return not_found");
    }
}
