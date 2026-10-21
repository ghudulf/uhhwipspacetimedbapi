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
        var routeRow = new RouteRow(8, "HTTP", "B", 4, 7, "00:10", true, 12, "Автобус", "A", "Водитель", "R-14", 4, 12.5, "Описание", "Тип", 0);
        var ticketRow = new TicketRow(9, 8, "HTTP — B", 2, 12.5m, "Cash", null, true, 13, "HTTP", "B", "00:10");
        var editCode = "conflict"; var receiptFound = false;
        var emptyChoices = false; var hideBuses = false;
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
                    "routes.list" => new CatalogPageResult<RouteRow>([routeRow], 1, 100, 1, true, true, true),
                    "tickets.list" => new CatalogPageResult<TicketRow>([ticketRow], 1, 100, 1, true, true, true),
                    "employees.get" => employee with { Name = "Актуально", Revision = 14 },
                    "maintenance.get" => editorMaintenance with { ServiceEngineer = "Актуально", Revision = 19 },
                    "routes.get" => routeRow with { StartPoint = "Актуально", Revision = 14 },
                    "tickets.get" => ticketRow with { TicketPrice = 44, Revision = 19 },
                    "employees.drivers" => new DriverLookupResult(emptyChoices ? [] : [new(4, "Водитель", "Синтетический", null, 2, "Driver", "", 1)], false),
                    "jobs.list" => new CatalogPageResult<JobRow>([new(4, "Должность", null, 1)], 1, 100, 1, false, false, false),
                    "buses.list" => new BusPageResult(emptyChoices ? [] : [new(7, "Автобус", "A", true, "Regular", 30, 2020, null, null, null, null, null, 1)], 1, 100, 1, false, false, false),
                    "employees.update" or "maintenance.update" or "routes.update" or "tickets.update" => new { code = editCode },
                    "mutations.receipt" => receiptFound ? new MutationResult(json.RootElement.GetProperty("operationId").GetString()!, "synthetic.update", 7, 20, DateTimeOffset.UtcNow) : null,
                    _ => new { code = "commit_outcome_unknown" }
                };
                context.Response.ContentType = "application/json";
                if (operation.EndsWith(".delete")) context.Response.StatusCode = 503;
                if (operation.EndsWith(".update")) context.Response.StatusCode = editCode == "conflict" ? 409 : 503;
                if (operation == "buses.list" && hideBuses) { context.Response.StatusCode = 403; response = new { code = "forbidden" }; }
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
            using var routes = new RouteManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var tickets = new TicketManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            await employees.ActivateAsync(); await records.ActivateAsync();
            await routes.ActivateAsync(); await tickets.ActivateAsync();
            Assert(employees.IsSynchronized && records.IsSynchronized && employees.Employees.Single().Row == employee && records.MaintenanceRecords.Single().Row == maintenance, "HTTP transport failed typed snapshot/sync gating");
            employees.SelectedEmployee = employees.Employees[0]; records.SelectedRecord = records.MaintenanceRecords[0];
            routes.SelectedRoute = routes.Routes[0]; tickets.SelectedTicket = tickets.Tickets[0];
            Assert(routes.IsSynchronized && tickets.IsSynchronized && routes.SelectedRoute.Row == routeRow && tickets.SelectedTicket.Row == ticketRow, "route/ticket HTTP snapshots ignored typed contracts");
            await employees.DeleteCommand.ExecuteAsync(null); await records.DeleteCommand.ExecuteAsync(null);
            await routes.DeleteCommand.ExecuteAsync(null); await tickets.DeleteCommand.ExecuteAsync(null);
            Assert(!employees.DeleteCommand.CanExecute(null) && !records.DeleteCommand.CanExecute(null), "uncertain HTTP delete allowed retry");
            Assert(!routes.DeleteCommand.CanExecute(null) && !tickets.DeleteCommand.CanExecute(null) && requests.Count(r => r.Operation.EndsWith(".delete")) == 4, "uncertain HTTP write replayed");
            foreach (var request in requests.Where(r => r.Operation.EndsWith(".delete")))
                Assert(!string.IsNullOrEmpty(request.OperationId) && request.Payload.GetProperty("expectedRevision").GetUInt64() == (request.Operation is "employees.delete" or "routes.delete" ? 12UL : 13UL), "HTTP delete lost operation ID or expected revision");
            await employees.LoadDataCommand.ExecuteAsync(null); await records.LoadDataCommand.ExecuteAsync(null);
            await routes.LoadDataCommand.ExecuteAsync(null); await tickets.LoadDataCommand.ExecuteAsync(null);
            Assert(employees.DeleteCommand.CanExecute(null) && records.DeleteCommand.CanExecute(null), "fresh HTTP snapshots did not reconcile uncertain deletes");
            Assert(routes.DeleteCommand.CanExecute(null) && tickets.DeleteCommand.CanExecute(null), "route/ticket fresh HTTP reads failed uncertain-delete reconciliation");
            var employeeOwner = new UserControl(); var employeeEditor = new EmployeeEditorService(new(employeeOwner));
            await ExerciseEditor(employeeOwner, ct => employeeEditor.ShowAsync(employee, () => true, ct), "Имя", "employees", 12, 14);
            var maintenanceOwner = new UserControl(); var maintenanceEditor = new MaintenanceEditorService(new(maintenanceOwner));
            await ExerciseEditor(maintenanceOwner, ct => maintenanceEditor.ShowAsync(editorMaintenance, () => true, ct), "Инженер", "maintenance", 13, 19);
            var routeOwner = new UserControl(); var routeEditor = new RouteEditorService(new(routeOwner));
            await ExerciseEditor(routeOwner, ct => routeEditor.ShowAsync(routeRow, () => true, ct), "Начальная точка", "routes", 12, 14);
            var ticketOwner = new UserControl(); var ticketEditor = new TicketEditorService(new(ticketOwner));
            await ExerciseEditor(ticketOwner, ct => ticketEditor.ShowAsync(ticketRow, () => true, ct), "Цена билета, BYN", "tickets", 13, 19, numeric: true);
            await LookupFailures();
            Console.WriteLine("PASS employees/maintenance production HTTP adapters: typed snapshots, sync eligibility, revision/operation-ID writes, no uncertain replay and fresh-snapshot delete reconciliation");
            Console.WriteLine("PASS real employee/maintenance editor services: shared HTTP lookups/update/get/receipt wiring, explicit conflict replacement, expected revisions, retained uncertain drafts/operation IDs across close/reopen and receipt-only settlement");
            Console.WriteLine("PASS routes/tickets production HTTP/editor matrix: typed snapshots, unknown deletes, retained conflict/uncertain drafts, receipt reconciliation, atomic revisioned active flag, empty driver/bus notices, visible-capacity guard and hidden-capacity server conflict");

            async Task ExerciseEditor(UserControl owner, Func<CancellationToken, Task<bool>> show, string fieldName, string resource, ulong originalRevision, ulong currentRevision, bool numeric = false)
            {
                editCode = "conflict"; receiptFound = false;
                Button Button(string text) => owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
                NumericUpDown Number(string label) => owner.GetLogicalDescendants().OfType<NumericUpDown>().Single(t => AutomationProperties.GetName(t) == label);
                bool Ready() => owner.GetLogicalDescendants().OfType<Control>().Any(t => (numeric ? t is NumericUpDown : t is TextBox) && AutomationProperties.GetName(t) == fieldName);
                string? Get() => numeric ? Number(fieldName).Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) : owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == fieldName).Text;
                void Set(string text) { if (numeric) Number(fieldName).Value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture); else owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == fieldName).Text = text; }
                void Deactivate() { if (resource == "routes") owner.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Активен").IsChecked = false; }
                void Click(string text) => Button(text).RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                async Task Until(Func<bool> check) { while (!check()) await Task.Delay(1, stop); }
                var dialog = show(stop);
                await Until(Ready);
                if (resource == "tickets")
                {
                    var before = requests.Count(r => r.Operation == "tickets.update"); Number("Номер места").Value = 31; Click("Сохранить");
                    await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("вместимость") == true));
                    Assert(requests.Count(r => r.Operation == "tickets.update") == before, "visible capacity violation reached transport"); Number("Номер места").Value = 2;
                }
                var firstDraft = numeric ? "33" : "Черновик"; var retained = numeric ? "55" : "Сохранённый черновик";
                Set(firstDraft); Deactivate(); Click("Сохранить");
                await Until(() => Button("Загрузить актуальные данные").IsVisible && Button("Загрузить актуальные данные").IsEnabled);
                Assert(Get() == firstDraft, "service conflict discarded draft");
                if (resource is "routes" or "tickets") Assert(owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(resource == "routes" ? "расписаниями" : "продажей") == true), "reference conflict lost Russian business explanation");
                Click("Загрузить актуальные данные");
                await Until(() => Button("Заменить черновик данными сервера").IsVisible);
                Assert(Get() == firstDraft, "service conflict read silently replaced draft");
                Click("Заменить черновик данными сервера"); Assert(Get() == (numeric ? "44" : "Актуально"), "service explicit replacement did not use get result");
                Set(retained); Deactivate(); editCode = "commit_outcome_unknown"; Click("Сохранить");
                await Until(() => Button("Проверить результат").IsVisible && Button("Проверить результат").IsEnabled);
                var attempts = requests.Where(r => r.Operation == resource + ".update").ToArray();
                Assert(attempts.Length == 2 && attempts[0].Payload.GetProperty("expectedRevision").GetUInt64() == originalRevision && attempts[1].Payload.GetProperty("expectedRevision").GetUInt64() == currentRevision && attempts[0].OperationId != attempts[1].OperationId, "service lost expected revisions or settled attempt ID rotation");
                if (resource == "routes") Assert(attempts.All(a => !a.Payload.GetProperty("isActive").GetBoolean() && a.Payload.GetProperty("routeNumber").GetString() == "R-14") && !requests.Any(r => r.Operation == "routes.set-active"), "route active flag was split from metadata update or lost on reload");
                Click("Закрыть"); Assert(!await dialog, "uncertain dialog close reported success");
                dialog = show(stop);
                await Until(Ready);
                Assert(Get() == retained && !Button("Сохранить").IsEnabled && Button("Проверить результат").IsVisible, "reopened service lost uncertain draft/block");
                Click("Проверить результат");
                await Until(() => Button("Проверить результат").IsEnabled);
                Assert(!dialog.IsCompleted && !Button("Сохранить").IsEnabled, "missing receipt enabled mutation replay");
                receiptFound = true; Click("Проверить результат"); Assert(await dialog, "service receipt did not settle uncertain attempt");
                Assert(requests.Count(r => r.Operation == resource + ".update") == 2 && requests.Last(r => r.Operation == "mutations.receipt").Payload.GetProperty("operationId").GetString() == attempts[1].OperationId, "service replayed uncertain update or reconciled another ID");
            }
            async Task LookupFailures()
            {
                async Task Until(Func<bool> check) { while (!check()) await Task.Delay(1, stop); }
                var owner = new UserControl(); var editor = new RouteEditorService(new(owner)); emptyChoices = true;
                var dialog = editor.ShowAsync(routeRow, () => true, stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Нет доступных водителей") == true && t.Text.Contains("Нет доступных автобусов")));
                var before = requests.Count(r => r.Operation == "routes.update");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(requests.Count(r => r.Operation == "routes.update") == before, "missing driver/bus choices allowed mutation");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog;
                emptyChoices = false; hideBuses = true; editCode = "conflict";
                owner = new UserControl(); var ticketsEditor = new TicketEditorService(new(owner)); dialog = ticketsEditor.ShowAsync(ticketRow, () => true, stop);
                await Until(() => owner.GetLogicalDescendants().OfType<NumericUpDown>().Any(n => AutomationProperties.GetName(n) == "Номер места"));
                owner.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "Номер места").Value = 31;
                before = requests.Count(r => r.Operation == "tickets.update");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("место недоступно") == true));
                Assert(requests.Count(r => r.Operation == "tickets.update") == before + 1, "hidden bus capacity was fabricated instead of relying on server conflict");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog; hideBuses = false;
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
