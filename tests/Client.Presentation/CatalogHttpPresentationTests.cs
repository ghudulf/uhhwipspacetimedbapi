using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

internal static class CatalogHttpPresentationTests
{
    public static async Task RunAsync(CancellationToken stop)
    {
        var transport = Environment.GetEnvironmentVariable("BRU_TRANSPORT");
        var api = ApiClientService.Instance;
        var cache = typeof(ApiClientService).GetField("_discoveredBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousOrigin = cache.GetValue(api);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{number}/"); listener.Start();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var requests = new List<(string Operation, JsonElement Payload, string? OperationId)>();
        var employee = new EmployeeRow(21, "HTTP", "Сотрудник", null, 4, null, 12, "Должность");
        var maintenance = new MaintenanceRow(22, 7, null, null, "HTTP", null, null, null, null, 13, "Автобус");
        var server = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(lifetime.Token); }
                catch (OperationCanceledException) { break; }
                var operation = context.Request.Url!.AbsolutePath.Split('/').Last();
                using var json = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: lifetime.Token);
                requests.Add((operation, json.RootElement.Clone(), context.Request.Headers["Idempotency-Key"]));
                object response = operation switch
                {
                    "employees.list" => new CatalogPageResult<EmployeeRow>([employee], 1, 100, 1, true, true, true),
                    "maintenance.list" => new CatalogPageResult<MaintenanceRow>([maintenance], 1, 100, 1, true, true, true),
                    _ => new { code = "commit_outcome_unknown" }
                };
                context.Response.ContentType = "application/json";
                if (operation.EndsWith(".delete")) context.Response.StatusCode = 503;
                await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, response.GetType(), ApiProtocol.Json, lifetime.Token);
                context.Response.Close();
            }
        }, lifetime.Token);
        try
        {
            Environment.SetEnvironmentVariable("BRU_TRANSPORT", "http");
            cache.SetValue(api, $"http://127.0.0.1:{number}/api/");
            using var employees = new EmployeeManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var records = new MaintenanceManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            await employees.ActivateAsync(); await records.ActivateAsync();
            Assert(employees.IsSynchronized && records.IsSynchronized && employees.Employees.Single().Row == employee && records.MaintenanceRecords.Single().Row == maintenance, "HTTP transport failed typed snapshot/sync gating");
            employees.SelectedEmployee = employees.Employees[0]; records.SelectedRecord = records.MaintenanceRecords[0];
            await employees.DeleteCommand.ExecuteAsync(null); await records.DeleteCommand.ExecuteAsync(null);
            Assert(!employees.DeleteCommand.CanExecute(null) && !records.DeleteCommand.CanExecute(null), "uncertain HTTP delete allowed retry");
            Assert(requests.Count(r => r.Operation.EndsWith(".delete")) == 2, "uncertain HTTP write replayed");
            foreach (var request in requests.Where(r => r.Operation.EndsWith(".delete")))
                Assert(!string.IsNullOrEmpty(request.OperationId) && request.Payload.GetProperty("expectedRevision").GetUInt64() == (request.Operation == "employees.delete" ? 12UL : 13UL), "HTTP delete lost operation ID or expected revision");
            await employees.LoadDataCommand.ExecuteAsync(null); await records.LoadDataCommand.ExecuteAsync(null);
            Assert(employees.DeleteCommand.CanExecute(null) && records.DeleteCommand.CanExecute(null), "fresh HTTP snapshots did not reconcile uncertain deletes");
            Assert(requests.All(r => r.Operation is "employees.list" or "maintenance.list" or "employees.delete" or "maintenance.delete"), "HTTP mode bypassed shared operation adapters");
            Console.WriteLine("PASS employees/maintenance production HTTP adapters: typed snapshots, sync eligibility, revision/operation-ID writes, no uncertain replay and fresh-snapshot delete reconciliation");
        }
        finally
        {
            lifetime.Cancel(); await server; listener.Stop();
            cache.SetValue(api, previousOrigin); Environment.SetEnvironmentVariable("BRU_TRANSPORT", transport);
        }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
