using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSalesApp.Services.Interfaces;

internal static class OperationCoverageTests
{
    private sealed record Endpoint(string Controller, string Method, string Verb, string Route)
    {
        public string Key => Controller + "|" + Method + "|" + Verb + "|" + Route;
    }
    private static ApplicationOperations Operations() => new(Stub.Create<ISpacetimeDBService>(), Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(),
        NullLogger<ApplicationOperations>.Instance, routes: Stub.Create<IRouteService>(), employees: Stub.Create<IEmployeeService>(), maintenance: Stub.Create<IMaintenanceService>());
    private static Endpoint[] Endpoints() => typeof(ApplicationOperations).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsDefined(typeof(NonActionAttribute), true))
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>(true).SelectMany(attribute =>
                (type.GetCustomAttributes<RouteAttribute>(true).Select(r => r.Template).DefaultIfEmpty("")).SelectMany(prefix => attribute.HttpMethods.Select(verb =>
                {
                    var template = attribute.Template ?? "";
                    var route = template.StartsWith("~/", StringComparison.Ordinal) || template.StartsWith('/') ? template.TrimStart('~', '/') : (prefix.Trim('/') + "/" + template.Trim('/')).Trim('/');
                    route = route.Replace("[controller]", type.Name.Replace("Controller", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase).Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase);
                    return new Endpoint(type.FullName!, method.Name, verb, route.Length == 0 ? "(conventional)" : "/" + route);
                })))))
        .DistinctBy(e => e.Key).OrderBy(e => e.Controller, StringComparer.Ordinal).ThenBy(e => e.Route, StringComparer.Ordinal).ThenBy(e => e.Verb, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal).ToArray();

    private static int Validate(string content)
    {
        var rows = content.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Select(line => line.Split('|').Select(c => c.Trim().Trim('`')).ToArray())
            .Where(columns => columns.Length == 7 && columns[1] != "Controller").ToArray();
        var coverage = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join("|", row.Skip(1).Take(4));
            if (!coverage.TryAdd(key, row[5])) throw new Exception("Duplicate endpoint coverage row: " + key);
            if (!Regex.IsMatch(row[5], @"\A(?:v2 [a-z]|adapter→[a-z]|HTTP-only: \S|legacy: \S)")) throw new Exception("Endpoint coverage has no truthful disposition: " + key);
        }
        var endpoints = Endpoints().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        if (!endpoints.SetEquals(coverage.Keys)) throw new Exception("Endpoint coverage drift. Missing: " + string.Join(", ", endpoints.Except(coverage.Keys)) + "; stale: " + string.Join(", ", coverage.Keys.Except(endpoints)));
        var operations = Operations();
        var descriptors = Regex.Matches(content, @"(?m)^\| v2 ([a-z]+\.[a-z.-]+) \| `([^`]+)` \| (true|false) \|\r?$")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value, bool.Parse(m.Groups[3].Value))).ToArray();
        var expected = operations.Capabilities.Select(c => (c.Name, c.Permission ?? "authenticated", c.Mutation)).ToArray();
        if (descriptors.Length != expected.Length || !descriptors.ToHashSet().SetEquals(expected)) throw new Exception("Shared operation capability ledger drift.");
        foreach (Match operation in Regex.Matches(content, @"(?:v2 |adapter→)([a-z]+\.[a-z][a-z.-]*)"))
            if (!operations.Contains(operation.Groups[1].Value)) throw new Exception("Coverage names an unregistered operation: " + operation.Groups[1].Value);
        if (content.Contains("Pending parity verification", StringComparison.OrdinalIgnoreCase)) throw new Exception("Coverage contains an unresolved placeholder.");
        return endpoints.Count;
    }
    public static void Run()
    {
        var content = File.ReadAllText(Path.Combine(Root(), "docs/architecture/operation-coverage.md"));
        var count = Validate(content);
        var first = content.Split('\n').First(line => line.StartsWith("| `", StringComparison.Ordinal));
        void Reject(string invalid)
        {
            try { Validate(invalid); } catch (Exception) { return; }
            throw new Exception("Coverage drift validator accepted an invalid ledger.");
        }
        Reject(content.Replace(first + "\n", "", StringComparison.Ordinal));
        var missingDisposition = first.Split('|'); missingDisposition[5] = " ";
        Reject(content.Replace(first, string.Join("|", missingDisposition), StringComparison.Ordinal));
        Reject(content + "\nadapter→unregistered.operation\n");
        Reject(content.Replace("| v2 jobs.get | `jobs.view` | false |\n", "", StringComparison.Ordinal));
        Console.WriteLine($"PASS coverage ledger drift: {count} reflected controller/method/verb/route endpoints, explicit dispositions, registered v2 references and negative drift cases");
    }

    public static void WriteLedger()
    {
        var operations = Operations();
        var builder = new StringBuilder("""
            # Application operation coverage

            ## Checkpoint — administrative operation parity

            This is a disposition ledger, not a blanket parity claim. Every reflected API
            controller method with an HTTP attribute is listed with its verb and combined
            controller/action route template. `legacy:` means that endpoint retains its
            existing service/projection path; an equivalent v2 query does not make it a
            shared-handler adapter. `HTTP-only:` identifies protocol, ceremony, page,
            upgrade or diagnostic boundaries rather than inventing a CRUD equivalent.

            Tickets, schedules, users, roles and permissions mutations now use the shared
            ApplicationOperations boundary through HTTP and resource/generic v1 socket
            adapters. IDs are normalized GUIDs; HTTP echoes `Operation-Id`; v1 requires a
            stable operation/request ID. Revisions and actor-scoped receipts are enforced
            by the module transaction. Legacy read response envelopes remain unchanged.

            QR display, approval, device enrollment and self-scan routes below retain their
            existing one-time protocol and tested client adapters. They are deliberately
            not recast as administrative CRUD operations. See [QR login](qr-login.md).

            The Api.Contracts drift test compares the complete endpoint set and checks
            every named v2 operation against runtime descriptors. After intentionally
            changing routes, regenerate with `dotnet run --project tests/Api.Contracts --
            --write-operation-ledger`, review each disposition, and commit the ledger.

            | Controller | Method | HTTP | Route | Disposition |
            |---|---|---|---|---|
            """);
        builder.AppendLine();
        foreach (var endpoint in Endpoints()) builder.AppendLine($"| `{endpoint.Controller}` | `{endpoint.Method}` | {endpoint.Verb} | `{endpoint.Route}` | {Disposition(endpoint)} |");
        builder.AppendLine("\n## Registered shared v2 operations\n\nThese descriptors are shared by the generic HTTP operation transport and v2 socket.\nResource-specific HTTP reads marked legacy above are not falsely claimed as adapters.\n\n| Operation | Permission | Mutation |\n|---|---|---|");
        foreach (var operation in operations.Capabilities) builder.AppendLine($"| v2 {operation.Name} | `{operation.Permission ?? "authenticated"}` | {operation.Mutation.ToString().ToLowerInvariant()} |");
        builder.AppendLine("\n## Remaining boundaries\n\nProvider ceremonies, OAuth/OIDC and account-security endpoints remain protocol-specific.\nLegacy controller reads and older fleet/catalog service adapters remain explicit in this ledger;\nthere is no unresolved parity-verification placeholder. Native screens are a later handoff.\nSee [realtime v2](realtime-v2.md) and [migration and validation](migration-and-validation.md).");
        File.WriteAllText(Path.Combine(Root(), "docs/architecture/operation-coverage.md"), builder.ToString());
        Console.WriteLine("Wrote reflected endpoint ledger; review dispositions before committing.");
    }
    private static string Disposition(Endpoint endpoint)
    {
        var controller = endpoint.Controller.Split('.').Last();
        var resources = new Dictionary<string, string> { ["TicketsController"] = "tickets", ["RouteSchedulesController"] = "schedules", ["UsersController"] = "users", ["RolesController"] = "roles", ["PermissionsController"] = "permissions", ["BusesController"] = "buses", ["TicketSalesController"] = "sales" };
        if (resources.TryGetValue(controller, out var resource))
        {
            if (endpoint.Method == "AssignRoleToUser") return "adapter→users.assign-role";
            if (endpoint.Method == "RemoveRoleFromUser") return "adapter→users.remove-role";
            if (endpoint.Method is "ActivateBus" or "DeactivateBus") return "adapter→buses.set-active";
            if (endpoint.Verb is "POST" or "PUT" or "DELETE")
            {
                var operation = resource + "." + (endpoint.Verb == "POST" ? "create" : endpoint.Verb == "PUT" ? "update" : "delete");
                return "adapter→" + operation + (endpoint.Verb == "PUT" && resource is "users" or "schedules" ? "; active-only payload adapter→" + resource + ".set-active" : "");
            }
            if (endpoint.Route.EndsWith("/realtime/ws", StringComparison.Ordinal)) return "HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers";
            return "legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract";
        }
        if (controller == "QrLoginController")
        {
            var detail = endpoint.Route.EndsWith("/complete", StringComparison.Ordinal) ? "enrolled P-256 proof and primary-factor policy" :
                endpoint.Route.Contains("/devices", StringComparison.Ordinal) ? "full bearer, own-device enrollment/list/revocation" :
                endpoint.Route.EndsWith("/approve", StringComparison.Ordinal) ? "full bearer, active account and verification code" :
                endpoint.Route.EndsWith("/status", StringComparison.Ordinal) ? "secret-bound exactly-once redemption" :
                endpoint.Route.EndsWith("/sessions", StringComparison.Ordinal) ? "anonymous QR challenge/PNG and bound-account decoys" : "secret-bound one-time QR confirmation/cancellation/denial";
            return "HTTP-only: QR ceremony; " + detail;
        }
        if (controller == "AuthController" && endpoint.Route.Contains("qr-login", StringComparison.Ordinal))
        {
            if (new[] { "/direct", "/status", "/cancel", "/notify" }.Any(suffix => endpoint.Route.EndsWith(suffix, StringComparison.Ordinal)))
                return "legacy: retired bearer-QR entry returns 410 with one-time QR replacement; feature-disabled remains 503";
            if (endpoint.Route.EndsWith("/generate", StringComparison.Ordinal) || endpoint.Route.EndsWith("/validate", StringComparison.Ordinal))
                return "legacy: encrypted bearer-QR compatibility endpoint; separate from the one-time QR protocol";
            return "HTTP-only: legacy QR HTML page; one-time QR clients use the dedicated ceremony routes";
        }
        if (controller.Contains("Auth", StringComparison.Ordinal) || controller.Contains("OAuth", StringComparison.Ordinal) || controller.Contains("OpenId", StringComparison.Ordinal))
            return "HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response";
        if (controller == "ApplicationOperationsController") return "HTTP-only: generic shared-operation dispatch transport; registered descriptors are listed below";
        if (controller == "SessionController") return "adapter→session.access";
        if (controller == "AdminController") return "HTTP-only: operator feature configuration, HTML or audit diagnostics";
        if (controller == "RealtimeController" || endpoint.Route.Contains("/realtime/", StringComparison.OrdinalIgnoreCase)) return "HTTP-only: realtime transport upgrade, event stream or connection diagnostics";
        if (controller.Contains("Report", StringComparison.Ordinal)) return "legacy: report/export HTTP adapter retains its established presentation contract";
        return "legacy: controller/service endpoint retained; no shared-handler parity claim";
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "README.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new Exception("Repository root not found.");
    }
}
