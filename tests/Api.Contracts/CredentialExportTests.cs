using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class CredentialExportTests
{
    public static async Task RunAsync()
    {
        var sentinel = "synthetic-export-sentinel-" + Guid.NewGuid().ToString("N");
        var user = new UserProfile { Login = "synthetic-export-user", PasswordHash = sentinel };
        var service = new ExportService(NullLogger<ExportService>.Instance);
        var exports = new[]
        {
            await service.ExportToJsonAsync(new[] { user }),
            Encoding.UTF8.GetString(await service.ExportToCsvAsync(new[] { user })),
            Encoding.UTF8.GetString(await service.ExportToExcelAsync(new[] { user }, "Users")),
            Encoding.UTF8.GetString(await service.ExportToPdfAsync(new[] { user }, "Users"))
        };
        foreach (var export in exports)
            if (export.Contains(sentinel) || export.Contains("PasswordHash") || !export.Contains(user.Login))
                throw new Exception("User export omitted profile data or included credential material.");
        var database = Stub.Create<ISpacetimeDBService>((method, _) => method.Name == "GetConnection" ? null : throw new Exception("Import attempted a database mutation."));
        var data = new DataService(database, NullLogger<DataService>.Instance);
        try { await data.AddAsync(user); throw new Exception("User profile import manufactured a credential."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("explicit password")) { }
        Console.WriteLine("PASS user JSON/CSV/Excel/PDF exports exclude credentials even for legacy input; profile import requires explicit registration password");
    }
}
