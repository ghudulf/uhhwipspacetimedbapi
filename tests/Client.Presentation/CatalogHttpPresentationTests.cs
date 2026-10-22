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
        var previousLogger = Serilog.Log.Logger; var outcomes = new Outcomes();
        using var logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(outcomes).CreateLogger();
        Serilog.Log.Logger = logger;
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
        var scheduleRow = ScheduleSalePresentationTests.Schedule with { Revision = 12 };
        var saleRow = ScheduleSalePresentationTests.Sale with { Revision = 13 };
        var userRow = AdministrationPresentationTests.User with { Revision = 12 };
        var roleRow = AdministrationPresentationTests.Role with { Revision = 12 };
        var permissionRow = AdministrationPresentationTests.Permission with { Revision = 13 };
        var editCode = "conflict"; var receiptFound = false;
        var emptyChoices = false; var hideBuses = false; var hideRoutes = false; var hideTickets = false;
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
                    "schedules.list" => new CatalogPageResult<ScheduleRow>([scheduleRow], 1, 100, 1, true, true, true),
                    "sales.list" => new CatalogPageResult<SaleRow>([saleRow], 1, 100, 1, true, true, true, 0, 1),
                    "users.list" => new CatalogPageResult<UserRow>([userRow], 1, 100, 1, true, true, true) { Access = AdministrationPresentationTests.Access },
                    "roles.list" => new CatalogPageResult<RoleRow>([roleRow], 1, 100, 1, true, true, true) { Access = AdministrationPresentationTests.Access },
                    "permissions.list" => new CatalogPageResult<PermissionRow>([permissionRow], 1, 100, 1, true, true, true) { Access = AdministrationPresentationTests.Access },
                    "employees.get" => employee with { Name = "Актуально", Revision = 14 },
                    "maintenance.get" => editorMaintenance with { ServiceEngineer = "Актуально", Revision = 19 },
                    "routes.get" => routeRow with { StartPoint = "Актуально", Revision = 14 },
                    "tickets.get" => ticketRow with { TicketPrice = 44, Revision = 19 },
                    "schedules.get" => scheduleRow with { StartPoint = "Актуально", Revision = 14 },
                    "sales.get" => saleRow with { BuyerName = "Актуально", Revision = 19 },
                    "users.get" => userRow with { Email = "Актуально", Revision = 14 },
                    "roles.get" => roleRow with { Name = "Актуально", Revision = 14 },
                    "permissions.get" => permissionRow with { Name = "Актуально", Revision = 19 },
                    "employees.drivers" => new DriverLookupResult(emptyChoices ? [] : [new(4, "Водитель", "Синтетический", null, 2, "Driver", "", 1)], false),
                    "jobs.list" => new CatalogPageResult<JobRow>([new(4, "Должность", null, 1)], 1, 100, 1, false, false, false),
                    "buses.list" => new BusPageResult(emptyChoices ? [] : [new(7, "Автобус", "A", true, "Regular", 30, 2020, null, null, null, null, null, 1)], 1, 100, 1, false, false, false),
                    "employees.update" or "maintenance.update" or "routes.update" or "tickets.update" or "schedules.update" or "sales.update" or "users.update" or "roles.update" or "permissions.update" or "users.set-active" or "users.assign-role" or "users.remove-role" or "users.create" => new { code = editCode },
                    "sales.create" => new MutationResult(context.Request.Headers["Idempotency-Key"]!, "sales.create", 25, 1, DateTimeOffset.UtcNow),
                    "mutations.receipt" => receiptFound ? new MutationResult(json.RootElement.GetProperty("operationId").GetString()!, "synthetic.update", 7, 20, DateTimeOffset.UtcNow) : null,
                    _ => new { code = "commit_outcome_unknown" }
                };
                context.Response.ContentType = "application/json";
                if (operation.EndsWith(".delete")) context.Response.StatusCode = 503;
                if (operation.EndsWith(".update")) context.Response.StatusCode = editCode == "conflict" ? 409 : 503;
                if (operation is "users.set-active" or "users.assign-role" or "users.remove-role" or "users.create") context.Response.StatusCode = editCode == "conflict" ? 409 : 503;
                if (operation == "buses.list" && hideBuses) { context.Response.StatusCode = 403; response = new { code = "forbidden" }; }
                if (operation == "routes.list" && hideRoutes || operation == "tickets.list" && hideTickets) { context.Response.StatusCode = 403; response = new { code = "forbidden" }; }
                try
                {
                    await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, response?.GetType() ?? typeof(object), ApiProtocol.Json, lifetime.Token);
                    context.Response.Close();
                }
                catch (HttpListenerException) { /* Browser deactivation intentionally aborts in-flight read responses. */ }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
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
            using var schedules = new RouteSchedulesManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var sales = new SalesManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var users = new UserManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var roles = new RoleManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            using var permissions = new PermissionManagementViewModel(null, confirmDelete: (_, _) => Task.FromResult(true));
            await employees.ActivateAsync(); await records.ActivateAsync();
            await routes.ActivateAsync(); await tickets.ActivateAsync();
            await schedules.ActivateAsync(); await sales.ActivateAsync();
            await users.ActivateAsync(); await roles.ActivateAsync(); await permissions.ActivateAsync();
            Assert(users.IsSynchronized && roles.IsSynchronized && permissions.IsSynchronized && users.Users.Single().Login == userRow.Login && users.SelectedUserPermissions.Length == 0, "administration HTTP typed snapshots failed");
            Assert(schedules.IsSynchronized && sales.IsSynchronized && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(schedules.Schedules.Single().Row), JsonSerializer.SerializeToElement(scheduleRow)) && sales.Sales.Single().Row == saleRow && sales.TotalIncome == null, "schedule/sale HTTP typed projection/aggregate failed");
            Assert(employees.IsSynchronized && records.IsSynchronized && employees.Employees.Single().Row == employee && records.MaintenanceRecords.Single().Row == maintenance, "HTTP transport failed typed snapshot/sync gating");
            employees.SelectedEmployee = employees.Employees[0]; records.SelectedRecord = records.MaintenanceRecords[0];
            routes.SelectedRoute = routes.Routes[0]; tickets.SelectedTicket = tickets.Tickets[0];
            schedules.SelectedSchedule = schedules.Schedules[0]; sales.SelectedSale = sales.Sales[0];
            users.SelectedUser = users.Users[0]; roles.SelectedItem = roles.Items[0]; permissions.SelectedItem = permissions.Items[0];
            Assert(users.SelectedUserPermissions.Single().Name == permissionRow.Name, "HTTP user permission detail projection dropped");
            Assert(routes.IsSynchronized && tickets.IsSynchronized && routes.SelectedRoute.Row == routeRow && tickets.SelectedTicket.Row == ticketRow, "route/ticket HTTP snapshots ignored typed contracts");
            await employees.DeleteCommand.ExecuteAsync(null); await records.DeleteCommand.ExecuteAsync(null);
            await routes.DeleteCommand.ExecuteAsync(null); await tickets.DeleteCommand.ExecuteAsync(null);
            await schedules.DeleteCommand.ExecuteAsync(null); await sales.DeleteCommand.ExecuteAsync(null);
            await users.DeleteCommand.ExecuteAsync(null); await roles.DeleteCommand.ExecuteAsync(null); await permissions.DeleteCommand.ExecuteAsync(null);
            Assert(!employees.DeleteCommand.CanExecute(null) && !records.DeleteCommand.CanExecute(null), "uncertain HTTP delete allowed retry");
            Assert(!routes.DeleteCommand.CanExecute(null) && !tickets.DeleteCommand.CanExecute(null) && !schedules.DeleteCommand.CanExecute(null) && !sales.DeleteCommand.CanExecute(null) && !users.DeleteCommand.CanExecute(null) && !roles.DeleteCommand.CanExecute(null) && !permissions.DeleteCommand.CanExecute(null) && requests.Count(r => r.Operation.EndsWith(".delete")) == 9, "uncertain HTTP write replayed");
            foreach (var request in requests.Where(r => r.Operation.EndsWith(".delete")))
                Assert(!string.IsNullOrEmpty(request.OperationId) && request.Payload.GetProperty("expectedRevision").GetUInt64() == (request.Operation is "employees.delete" or "routes.delete" or "schedules.delete" or "users.delete" or "roles.delete" ? 12UL : 13UL), "HTTP delete lost operation ID or expected revision");
            await employees.LoadDataCommand.ExecuteAsync(null); await records.LoadDataCommand.ExecuteAsync(null);
            await routes.LoadDataCommand.ExecuteAsync(null); await tickets.LoadDataCommand.ExecuteAsync(null);
            await schedules.LoadDataCommand.ExecuteAsync(null); await sales.LoadDataCommand.ExecuteAsync(null);
            await users.LoadDataCommand.ExecuteAsync(null); await roles.LoadDataCommand.ExecuteAsync(null); await permissions.LoadDataCommand.ExecuteAsync(null);
            Assert(users.DeleteCommand.CanExecute(null) && roles.DeleteCommand.CanExecute(null) && permissions.DeleteCommand.CanExecute(null), "administration HTTP uncertain deletes did not reconcile");
            Assert(schedules.DeleteCommand.CanExecute(null) && sales.DeleteCommand.CanExecute(null), "schedule/sale fresh HTTP snapshot did not reconcile delete");
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
            var scheduleOwner = new UserControl(); var scheduleEditor = new ScheduleEditorService(new(scheduleOwner));
            await ExerciseEditor(scheduleOwner, ct => scheduleEditor.ShowAsync(scheduleRow, () => true, ct), "Начальная остановка", "schedules", 12, 14);
            var saleOwner = new UserControl(); var saleEditor = new SaleEditorService(new(saleOwner));
            await ExerciseEditor(saleOwner, ct => saleEditor.ShowAsync(saleRow, () => true, ct), "Покупатель", "sales", 13, 19);
            var userOwner = new UserControl(); var userEditor = new UserEditorService(new(userOwner));
            await ExerciseEditor(userOwner, ct => userEditor.ShowAsync(userRow, () => true, ct), "Email", "users", 12, 14);
            var roleOwner = new UserControl(); var roleEditor = new RoleEditorService(new(roleOwner), () => true);
            await ExerciseEditor(roleOwner, ct => roleEditor.ShowAsync(roleRow, () => true, ct), "Название роли", "roles", 12, 14);
            var permissionOwner = new UserControl(); var permissionEditor = new PermissionEditorService(new(permissionOwner));
            await ExerciseEditor(permissionOwner, ct => permissionEditor.ShowAsync(permissionRow, () => true, ct), "Название разрешения", "permissions", 13, 19);
            await LookupFailures();
            await SaleDefaults();
            await AdministrationSecurity();
            await AdministrationBrowsers();
            foreach (var rowType in new[] { "ScheduleRow", "SaleRow", "UserRow", "RoleRow", "PermissionRow" })
            {
                var events = outcomes.Events.Where(e => e.Properties.TryGetValue("RowType", out var row) && row.ToString().Contains(rowType) && e.Properties.ContainsKey("OperationId") && e.Properties.ContainsKey("Revision") && e.Properties.ContainsKey("EntityId")).ToArray();
                foreach (var code in new[] { "conflict", "commit_outcome_unknown", "ok" })
                    Assert(events.Any(e => e.Properties["Code"].ToString().Contains(code)), rowType + " editor outcome log lost code/ID/revision/entity");
                File.WriteAllLines("/tmp/opencode/" + (rowType is "ScheduleRow" or "SaleRow" ? "h15-" : "h16-") + rowType + "-editor-outcomes.log", events.Select(e => e.RenderMessage()));
            }
            Console.WriteLine("PASS employees/maintenance production HTTP adapters: typed snapshots, sync eligibility, revision/operation-ID writes, no uncertain replay and fresh-snapshot delete reconciliation");
            Console.WriteLine("PASS real employee/maintenance editor services: shared HTTP lookups/update/get/receipt wiring, explicit conflict replacement, expected revisions, retained uncertain drafts/operation IDs across close/reopen and receipt-only settlement");
            Console.WriteLine("PASS routes/tickets production HTTP/editor matrix: typed snapshots, unknown deletes, retained conflict/uncertain drafts, receipt reconciliation, atomic revisioned active flag, empty driver/bus notices, visible-capacity guard and hidden-capacity server conflict");
            Console.WriteLine("PASS schedules/sales production HTTP/editor matrix: typed filtered snapshots, unknown amount, uncertain-delete reconciliation, retained drafts/IDs on reopen, receipt-only settlement, get-based conflict replacement, atomic schedule metadata/activation, immutable sale date/ticket, buyer defaults/empty phone and lookup permission degradation");
            Console.WriteLine("PASS users/roles/permissions production HTTP/editor matrix: typed snapshots, current access, uncertain deletes, retained drafts/IDs, get/receipt reconciliation, atomic permission payload, masked transient initial password excluded from all sinks in full mode, duplicate login/validation, activation/privilege refusal and protected-role disabled accessible controls");

            async Task ExerciseEditor(UserControl owner, Func<CancellationToken, Task<bool>> show, string fieldName, string resource, ulong originalRevision, ulong currentRevision, bool numeric = false)
            {
                editCode = "conflict"; receiptFound = false;
                Button Button(string text) => owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
                NumericUpDown Number(string label) => owner.GetLogicalDescendants().OfType<NumericUpDown>().Single(t => AutomationProperties.GetName(t) == label);
                bool Ready() => owner.GetLogicalDescendants().OfType<Control>().Any(t => (numeric ? t is NumericUpDown : t is TextBox) && AutomationProperties.GetName(t) == fieldName);
                string? Get() => numeric ? Number(fieldName).Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) : owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == fieldName).Text;
                void Set(string text) { if (numeric) Number(fieldName).Value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture); else owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == fieldName).Text = text; }
                void Deactivate() { if (resource is "routes" or "schedules") owner.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Активен").IsChecked = false; }
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
                if (resource == "schedules") Assert(attempts.All(a => !a.Payload.GetProperty("isActive").GetBoolean() && a.Payload.GetProperty("routeStops").GetArrayLength() == 2 && a.Payload.GetProperty("estimatedStopTimes").GetArrayLength() == 2 && a.Payload.GetProperty("stopDistances").GetArrayLength() == 2 && a.Payload.GetProperty("notes").GetString() == "Примечание") && !requests.Any(r => r.Operation == "schedules.set-active"), "schedule metadata/activation split or dropped");
                if (resource == "sales") Assert(attempts.All(a => !a.Payload.TryGetProperty("saleDate", out _) && !a.Payload.TryGetProperty("ticketId", out _) && a.Payload.GetProperty("buyerPhone").GetString() == ""), "sale date/ticket editable or empty phone changed");
                if (resource == "users") Assert(attempts.All(a => !a.Payload.TryGetProperty("initialPassword", out _) && !a.Payload.TryGetProperty("isActive", out _) && !a.Payload.TryGetProperty("login", out _)), "profile update admitted credential/login/activation");
                if (resource == "roles") Assert(attempts.All(a => a.Payload.GetProperty("permissionIds").EnumerateArray().Select(p => p.GetUInt32()).SequenceEqual(new[] { 7U })), "role update lost atomic permission set");
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
            async Task AdministrationBrowsers()
            {
                foreach (var resource in new[] { "roles", "permissions" })
                {
                    var owner = new UserControl(); var host = new ControlDialogHost(owner);
                    var browser = resource == "roles" ? AdministrationCatalogDialog.ShowRolesAsync(host, stop) : AdministrationCatalogDialog.ShowPermissionsAsync(host, stop);
                    while (!owner.GetLogicalDescendants().OfType<DataGrid>().Any()) await Task.Delay(1, stop);
                    foreach (var label in new[] { "Назад", "Далее", "Обновить", "Добавить", "Редактировать", "Удалить" })
                    {
                        var button = owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == label);
                        Assert(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button)?.GetName() == label && button.Focusable, "administrative browser runtime action/pager has no accessible name/focus");
                    }
                    var add = owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Добавить");
                    Assert(add.Command?.CanExecute(null) == true, resource + " browser add ineligible");
                    add.Command!.Execute(null);
                    var field = resource == "roles" ? "Название роли" : "Название разрешения";
                    while (!owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field))
                    {
                        var error = outcomes.Events.LastOrDefault(e => e.MessageTemplate.Text.Contains("editor stopped"));
                        if (error != null) throw new Exception(resource + " browser nested editor failed: " + error.RenderMessage());
                        await Task.Delay(1, stop);
                    }
                    owner.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    while (owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field)) await Task.Delay(1, stop);
                    var grid = owner.GetLogicalDescendants().OfType<DataGrid>().Single(); grid.SelectedItem = grid.ItemsSource!.Cast<object>().First();
                    while (!owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Редактировать").IsEnabled) await Task.Delay(1, stop);
                    owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Редактировать").Command!.Execute(null);
                    while (!owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field)) await Task.Delay(1, stop);
                    owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == field).Text = "Сохранённый браузерный черновик";
                    editCode = "commit_outcome_unknown"; receiptFound = false; var before = requests.Count(r => r.Operation == resource + ".update");
                    owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    while (!owner.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Проверить результат" && b.IsVisible && b.IsEnabled)) await Task.Delay(1, stop);
                    owner.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    while (owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field)) await Task.Delay(1, stop);
                    owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await browser;
                    browser = resource == "roles" ? AdministrationCatalogDialog.ShowRolesAsync(host, stop) : AdministrationCatalogDialog.ShowPermissionsAsync(host, stop);
                    while (!owner.GetLogicalDescendants().OfType<DataGrid>().Any()) await Task.Delay(1, stop);
                    while (!owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Редактировать").IsEnabled) await Task.Delay(1, stop);
                    owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Редактировать").Command!.Execute(null);
                    while (!owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field)) await Task.Delay(1, stop);
                    Assert(owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == field).Text == "Сохранённый браузерный черновик" && !owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").IsEnabled, "closing/reopening catalog browser lost uncertain draft/ID block");
                    receiptFound = true; owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Проверить результат").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    while (owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == field)) await Task.Delay(1, stop);
                    Assert(requests.Count(r => r.Operation == resource + ".update") == before + 1, "reopened catalog browser replayed uncertain mutation");
                    owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await browser;
                }
            }
            async Task AdministrationSecurity()
            {
                const string secret = "h16-initial-password-must-never-reach-a-sink";
                var previousTraffic = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC"); Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "full");
                try
                {
                    var owner = new UserControl(); var editor = new UserEditorService(new(owner)); editCode = "conflict"; receiptFound = false;
                    TextBox Field(string label) => owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == label);
                    Button Button(string label) => owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == label);
                    void Click(string label) => Button(label).RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                    async Task Until(Func<bool> predicate) { while (!predicate()) await Task.Delay(1, stop); }
                    var dialog = editor.ShowAsync(null, () => true, stop); await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Начальный пароль"));
                    Field("Логин").Text = "VisibleCreate"; var password = Field("Начальный пароль"); Assert(password.PasswordChar != '\0', "initial password box not masked");
                    password.Text = "short"; Click("Сохранить"); await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("6–1024") == true));
                    Assert(!requests.Any(r => r.Operation == "users.create"), "invalid initial password dispatched");
                    password.Text = secret; Click("Сохранить"); await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Логин уже занят") == true));
                    Assert(Field("Логин").Text == "VisibleCreate" && Button("Сохранить").IsEnabled, "duplicate login discarded correctable draft");
                    Field("Логин").Text = "VisibleCreateCorrected"; editCode = "commit_outcome_unknown"; Click("Сохранить"); await Until(() => Button("Проверить результат").IsVisible && Button("Проверить результат").IsEnabled);
                    var attempts = requests.Where(r => r.Operation == "users.create").ToArray(); Assert(attempts.Length == 2 && attempts[0].OperationId != attempts[1].OperationId && attempts.All(a => a.Payload.GetProperty("initialPassword").GetString() == secret), "initial credential wire shape or corrected-create attempt IDs wrong");
                    Click("Закрыть"); Assert(!await dialog && password.Text == "" && (string)typeof(UserEditorService).GetField("_initialPassword", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)! == "", "closed create retained password");
                    dialog = editor.ShowAsync(null, () => true, stop); await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Начальный пароль"));
                    Assert(Field("Начальный пароль").Text == "" && Field("Логин").Text == "VisibleCreateCorrected" && !Button("Сохранить").IsEnabled, "uncertain reopening retained password or lost safe draft/block");
                    receiptFound = true; Click("Проверить результат"); Assert(await dialog && requests.Count(r => r.Operation == "users.create") == 2, "create receipt reconciliation replayed initial password");
                    Assert((string)typeof(UserEditorService).GetField("_initialPassword", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)! == "", "successful close retained initial password");
                    Assert(!outcomes.Events.Any(e => e.RenderMessage().Contains(secret) || e.Properties.Values.Any(v => v.ToString().Contains(secret))), "initial password substring reached a capture sink");
                    foreach (var protectedRole in new[] { roleRow with { IsSystem = true }, roleRow with { Name = "Administrator" } })
                    {
                        owner = new UserControl(); var rolesEditor = new RoleEditorService(new(owner), () => true); dialog = rolesEditor.ShowAsync(protectedRole, () => true, stop);
                        await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Название роли"));
                        Assert(!Button("Сохранить").IsEnabled && owner.GetLogicalDescendants().OfType<TextBox>().All(t => !t.IsEnabled) && owner.GetLogicalDescendants().OfType<NumericUpDown>().All(n => !n.IsEnabled) && owner.GetLogicalDescendants().OfType<ListBox>().All(l => !l.IsEnabled), "Administrator/system role controls not disabled");
                        foreach (var control in owner.GetLogicalDescendants().OfType<Control>().Where(c => c is TextBox or NumericUpDown or ListBox)) Assert(!string.IsNullOrEmpty(AutomationProperties.GetName(control)), "disabled administrative field lost accessible name");
                        Click("Закрыть"); await dialog;
                    }
                    owner = new UserControl(); var assignments = new RoleAssignmentService(new(owner));
                    await Action(() => assignments.ShowActivationAsync(userRow, () => true, stop), "users.set-active", "Пользователь активен");
                    await Action(() => assignments.ShowAsync(userRow, roleRow with { Name = "Administrator" }, false, () => true, stop), "users.assign-role", null);
                    await Action(() => assignments.ShowAsync(userRow, roleRow, true, () => true, stop), "users.remove-role", null);

                    async Task Action(Func<Task<bool>> show, string operation, string? activeLabel)
                    {
                        editCode = "conflict"; receiptFound = false; var actionDialog = show();
                        await Until(() => owner.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Сохранить"));
                        if (activeLabel != null) owner.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Content as string == activeLabel).IsChecked = false;
                        Click("Сохранить"); await Until(() => Button("Загрузить актуальные данные").IsVisible && Button("Загрузить актуальные данные").IsEnabled);
                        Assert(owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("[conflict]") == true && t.Text.Contains("последнего активного администратора") && t.Text.Contains("Administrator")), "security refusal lost server code/Russian explanation");
                        Click("Загрузить актуальные данные"); await Until(() => Button("Заменить черновик данными сервера").IsVisible); Click("Заменить черновик данными сервера");
                        editCode = "commit_outcome_unknown"; Click("Сохранить"); await Until(() => Button("Проверить результат").IsVisible && Button("Проверить результат").IsEnabled);
                        var sent = requests.Where(r => r.Operation == operation).ToArray(); Assert(sent.Length == 2 && sent[0].OperationId != sent[1].OperationId && sent[0].Payload.GetProperty("expectedRevision").GetUInt64() == 12 && sent[1].Payload.GetProperty("expectedRevision").GetUInt64() == 14, "administrative action dropped conflict revision/ID");
                        Click("Закрыть"); Assert(!await actionDialog, "uncertain action close committed"); actionDialog = show(); await Until(() => owner.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Проверить результат"));
                        Assert(!Button("Сохранить").IsEnabled, "reopened activation/assignment allowed uncertain replay"); receiptFound = true; Click("Проверить результат"); Assert(await actionDialog && requests.Count(r => r.Operation == operation) == 2, "administrative action receipt replayed mutation");
                    }
                }
                finally { Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", previousTraffic); }
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
                hideRoutes = true;
                owner = new UserControl(); var scheduleEditor = new ScheduleEditorService(new(owner)); dialog = scheduleEditor.ShowAsync(scheduleRow, () => true, stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Нет права просмотра маршрутов") == true));
                before = requests.Count(r => r.Operation == "schedules.update"); owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(requests.Count(r => r.Operation == "schedules.update") == before, "hidden schedule route fabricated");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog; hideRoutes = false;
                hideTickets = true; owner = new UserControl(); var saleEditor = new SaleEditorService(new(owner)); dialog = saleEditor.ShowAsync(null, () => true, stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Нет права просмотра билетов") == true));
                before = requests.Count(r => r.Operation == "sales.create"); owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(requests.Count(r => r.Operation == "sales.create") == before, "hidden sale ticket fabricated");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog;
                owner = new UserControl(); saleEditor = new SaleEditorService(new(owner)); var ticketReads = requests.Count(r => r.Operation == "tickets.list"); dialog = saleEditor.ShowAsync(saleRow, () => true, stop);
                await Until(() => owner.GetLogicalDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Покупатель"));
                Assert(requests.Count(r => r.Operation == "tickets.list") == ticketReads, "buyer-only edit unnecessarily required ticket permission");
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog; hideTickets = false;
            }
            async Task SaleDefaults()
            {
                var owner = new UserControl(); var editor = new SaleEditorService(new(owner)); var dialog = editor.ShowAsync(null, () => true, stop);
                while (!owner.GetLogicalDescendants().OfType<ComboBox>().Any()) await Task.Delay(1, stop);
                owner.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedIndex = 0;
                var name = owner.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Покупатель");
                Assert(name.Text == "ФИЗ.ПРОДАЖА", "default buyer missing from real editor"); name.Text = "";
                owner.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(await dialog, "sale default create failed"); var request = requests.Single(r => r.Operation == "sales.create");
                Assert(request.Payload.GetProperty("buyerName").GetString() == "ФИЗ.ПРОДАЖА" && request.Payload.GetProperty("buyerPhone").GetString() == "" && request.Payload.GetProperty("location").GetString() == "POS" && !request.Payload.TryGetProperty("saleDate", out _), "sale create lost defaults or admitted client date");
            }
        }
        finally
        {
            lifetime.Cancel(); await server; listener.Stop();
            cache.SetValue(api, previousOrigin); Environment.SetEnvironmentVariable("BRU_TRANSPORT", transport);
            Serilog.Log.Logger = previousLogger;
        }
    }
    private sealed class Outcomes : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<Serilog.Events.LogEvent> Events { get; } = new();
        public void Emit(Serilog.Events.LogEvent value) => Events.Enqueue(value);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
