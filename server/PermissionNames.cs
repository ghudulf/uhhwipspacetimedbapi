namespace BRU.Avtopark.Security;

/// <summary>Shared source compiled into the API gateway and pinned WASI module. Stored IDs/grants remain intact.</summary>
public static class PermissionNames
{
    public static string Canonical(string name)
    {
        name = name switch
        {
            "create_bus" => "buses.create",
            "create_ticket" => "tickets.create",
            "cancel_ticket" => "tickets.cancel",
            "schedule_maintenance" => "maintenance.create",
            // Role assignment and permission granting are separate privileges; never fold them into edit.
            _ => name
        };
        return name.EndsWith(".update", System.StringComparison.Ordinal) ? name.Substring(0, name.Length - 7) + ".edit" : name;
    }
}
