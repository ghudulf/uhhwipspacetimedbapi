using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
        var date = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        var editorMaintenance = maintenance with { LastServiceDate = date, NextServiceDate = date.AddDays(1) };
        var editCode = "conflict"; var receiptFound = false;
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
                object? response = operation switch
                {
                    "employees.list" => new CatalogPageResult<EmployeeRow>([employee], 1, 100, 1, true, true, true),
                    "maintenance.list" => new CatalogPageResult<MaintenanceRow>([maintenance], 1, 100, 1, true, true, true),
                    "employees.get" => employee with { Name = "Актуально", Revision = 14 },
                    "maintenance.get" => editorMaintenance with { ServiceEngineer = "Актуально", Revision = 19 },
                    "jobs.list" => new CatalogPageResult<JobRow>([new(4, "Должность", null, 1)], 1, 100, 1, false, false, false),
                    "buses.list" => new BusPageResult([new(7, "Автобус", "A", true, "Regular", 30, 2020, null, null, null, null, null, 1)], 1, 100, 1, false, false, false),
                    "employees.update" or "maintenance.update" => new { code = editCode },
                    "mutations.receipt" => receiptFound ? new MutationResult(json.RootElement.GetProperty("operationId").GetString()!, "synthetic.update", 7, 20, DateTimeOffset.UtcNow) : null,
                    _ => new { code = "commit_outcome_unknown" }
                };
                context.Response.ContentType = "application/json";
                if (operation.EndsWith(".delete")) context.Response.StatusCode = 503;
                if (operation.EndsWith(".update")) context.Response.StatusCode = editCode == "conflict" ? 409 : 503;
                await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, response?.GetType() ?? typeof(object), ApiProtocol.Json, lifetime.Token);
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
            var employeeOwner = new UserControl(); var employeeEditor = new EmployeeEditorService(new(employeeOwner));
            await ExerciseEditor(employeeOwner, ct => employeeEditor.ShowAsync(employee, () => true, ct), "Имя", "employees", 12, 14);
            var maintenanceOwner = new UserControl(); var maintenanceEditor = new MaintenanceEditorService(new(maintenanceOwner));
            await ExerciseEditor(maintenanceOwner, ct => maintenanceEditor.ShowAsync(editorMaintenance, () => true, ct), "Инженер", "maintenance", 13, 19);
            Console.WriteLine("PASS employees/maintenance production HTTP adapters: typed snapshots, sync eligibility, revision/operation-ID writes, no uncertain replay and fresh-snapshot delete reconciliation");
            Console.WriteLine("PASS real employee/maintenance editor services: shared HTTP lookups/update/get/receipt wiring, explicit conflict replacement, expected revisions, retained uncertain drafts/operation IDs across close/reopen and receipt-only settlement");

            async Task ExerciseEditor(UserControl owner, Func<CancellationToken, Task<bool>> show, string fieldName, string resource, ulong originalRevision, ulong currentRevision)
            {
                editCode = "conflict"; receiptFound = false;
                Button Button(string text) => owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
                TextBox Field() => owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == fieldName);
                void Click(string text) => Button(text).RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                async Task Until(Func<bool> check) { while (!check()) await Task.Delay(1, stop); }
                var dialog = show(stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == fieldName));
                Field().Text = "Черновик"; Click("Сохранить");
                await Until(() => Button("Загрузить актуальные данные").IsVisible && Button("Загрузить актуальные данные").IsEnabled);
                Assert(Field().Text == "Черновик", "service conflict discarded draft");
                Click("Загрузить актуальные данные");
                await Until(() => Button("Заменить черновик данными сервера").IsVisible);
                Assert(Field().Text == "Черновик", "service conflict read silently replaced draft");
                Click("Заменить черновик данными сервера"); Assert(Field().Text == "Актуально", "service explicit replacement did not use get result");
                Field().Text = "Сохранённый черновик"; editCode = "commit_outcome_unknown"; Click("Сохранить");
                await Until(() => Button("Проверить результат").IsVisible && Button("Проверить результат").IsEnabled);
                var attempts = requests.Where(r => r.Operation == resource + ".update").ToArray();
                Assert(attempts.Length == 2 && attempts[0].Payload.GetProperty("expectedRevision").GetUInt64() == originalRevision && attempts[1].Payload.GetProperty("expectedRevision").GetUInt64() == currentRevision && attempts[0].OperationId != attempts[1].OperationId, "service lost expected revisions or settled attempt ID rotation");
                Click("Закрыть"); Assert(!await dialog, "uncertain dialog close reported success");
                dialog = show(stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == fieldName));
                Assert(Field().Text == "Сохранённый черновик" && !Button("Сохранить").IsEnabled && Button("Проверить результат").IsVisible, "reopened service lost uncertain draft/block");
                Click("Проверить результат");
                await Until(() => Button("Проверить результат").IsEnabled);
                Assert(!dialog.IsCompleted && !Button("Сохранить").IsEnabled, "missing receipt enabled mutation replay");
                receiptFound = true; Click("Проверить результат"); Assert(await dialog, "service receipt did not settle uncertain attempt");
                Assert(requests.Count(r => r.Operation == resource + ".update") == 2 && requests.Last(r => r.Operation == "mutations.receipt").Payload.GetProperty("operationId").GetString() == attempts[1].OperationId, "service replayed uncertain update or reconciled another ID");
            }
        }
        finally
        {
            lifetime.Cancel(); await server; listener.Stop();
            cache.SetValue(api, previousOrigin); Environment.SetEnvironmentVariable("BRU_TRANSPORT", transport);
        }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
