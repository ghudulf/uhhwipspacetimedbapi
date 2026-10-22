using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class BootstrapTests
{
    public static async Task RunAsync(SpacetimeDBService owner, Identity ownerIdentity, string ownerToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bru-bootstrap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var credentialFile = Path.Combine(directory, "api.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var body = new StringContent("[]", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var reset = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_bootstrap_test", body, deadline.Token);
        reset.EnsureSuccessStatusCode();
        SpacetimeDBService Create() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SpacetimeDB:Host"] = "http://127.0.0.1:3000", ["SpacetimeDB:DatabaseName"] = "bru-architecture-integration",
            ["SpacetimeDB:CredentialFile"] = credentialFile, ["SpacetimeDB:LogReducerLogsToFile"] = "false"
        }).Build(), NullLogger<SpacetimeDBService>.Instance);
        var api = Create();
        var pumping = true;
        var pump = Task.Run(async () => { while (!deadline.IsCancellationRequested) { if (pumping) api.ProcessFrameTick(); await Task.Delay(10, deadline.Token); } });
        Identity? apiIdentity = null;
        try
        {
            api.Connect();
            await Until(() => api.IsSubscriptionReady(), deadline.Token);
            apiIdentity = api.GetLocalIdentity()!.Value;
            Assert(apiIdentity != ownerIdentity, "first API connection reused deployment owner");
            var stored = DatabaseCredentialFile.Read(credentialFile, "http://127.0.0.1:3000", "bru-architecture-integration")!;
            Assert(stored.Identity == apiIdentity.ToString() && stored.Token != ownerToken, "API credential was not persisted separately");
            if (!OperatingSystem.IsWindows()) Assert((File.GetUnixFileMode(credentialFile) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "API credential file is not private");
            RejectFile(() => DatabaseCredentialFile.Read(credentialFile, "http://127.0.0.1:3000", "another-database"));
            RejectFile(() => DatabaseCredentialFile.Save(credentialFile, "http://127.0.0.1:3000", "bru-architecture-integration", ownerIdentity.ToString(), ownerToken));
            await Reject(() => api.ExecuteReducerAsync(new Reducer.BootstrapInstallation(apiIdentity.Value, "synthetic-owner", "untrusted-password-value"), _ => true));
            await Reject(() => owner.ExecuteReducerAsync(new Reducer.BootstrapInstallation(apiIdentity.Value, "synthetic-owner", "short"), _ => true));
            Assert(!await owner.ReadAsync(c => c.Db.TrustedApiIdentity.Identity.Find(apiIdentity.Value) != null), "rejected password partially granted API trust");
            var password = "synthetic-initial-" + Guid.NewGuid().ToString("N");
            var original = await owner.ReadAsync(c => c.Db.UserProfile.UserId.Find(ownerIdentity)!);
            await owner.ExecuteReducerAsync(new Reducer.BootstrapInstallation(apiIdentity.Value, "synthetic-owner", password), _ => true);
            var provisioned = await owner.ReadAsync(c => c.Db.UserProfile.UserId.Find(ownerIdentity)!);
            Assert(provisioned.UserId == original.UserId && provisioned.LegacyUserId == original.LegacyUserId, "bootstrap changed account identity");
            await UntilAsync(async () => await api.ReadAsync(c => c.Db.UserProfile.Login.Find("synthetic-owner") != null), deadline.Token);
            var authentication = new AuthenticationService(api, new RoleService(api, NullLogger<RoleService>.Instance), NullLogger<AuthenticationService>.Instance);
            Assert((await authentication.AuthenticateAsync("synthetic-owner", password))?.UserId == ownerIdentity, "initial password cannot log in");
            var failures = 0;
            void ObserveFailure(EventContext _, AuthenticationEvent evt)
            {
                if (evt.EventType == "Failed") Interlocked.Increment(ref failures);
            }
            await api.ReadAsync(c => { c.Db.AuthenticationEvent.OnInsert += ObserveFailure; return true; });
            Assert(await authentication.AuthenticateAsync("synthetic-owner", "incorrect-password") == null, "incorrect password returned authenticated profile");
            await Until(() => Volatile.Read(ref failures) > 0, deadline.Token);
            var observedFailures = Volatile.Read(ref failures);
            await Reject(() => api.ExecuteReducerAsync(new Reducer.RecordAuthenticationFailure("synthetic-owner", password), _ => true));
            Assert(Volatile.Read(ref failures) == observedFailures, "valid credentials forged a failure event");
            await api.ReadAsync(c => { c.Db.AuthenticationEvent.OnInsert -= ObserveFailure; return true; });
            Assert((await authentication.AuthenticateAsync("synthetic-owner", password))?.UserId == ownerIdentity,
                "correct password stopped working after a failed attempt");
            Console.WriteLine("PASS checked authentication rejects bad passwords while committing independently reverified failure audits; valid credentials cannot forge failures");
            Assert(await authentication.AuthenticateDirectQRAsync("synthetic-owner", "unbound-proof") == null, "unbound QR proof returned authenticated profile");
            await Reject(() => owner.ExecuteReducerAsync(new Reducer.BootstrapInstallation(apiIdentity.Value, "synthetic-owner", "replacement-password-value"), _ => true));
            Assert((await authentication.AuthenticateAsync("synthetic-owner", password))?.UserId == ownerIdentity, "bootstrap overwrote existing password");
            Assert(await api.ReadAsync(c => c.Db.UserProfile.Iter().All(u => string.IsNullOrEmpty(u.PasswordHash))), "bootstrap exposed credentials in public profiles");
            await owner.ExecuteReducerAsync(new Reducer.BootstrapInstallation(apiIdentity.Value, null, null), _ => true);
            pumping = false;
            api.Disconnect();
            api = Create();
            api.Connect();
            pumping = true;
            await Until(() => api.IsSubscriptionReady(), deadline.Token);
            Assert(api.GetLocalIdentity() == apiIdentity, "API restart generated a different identity");
            authentication = new AuthenticationService(api, new RoleService(api, NullLogger<RoleService>.Instance), NullLogger<AuthenticationService>.Instance);
            Assert((await authentication.AuthenticateAsync("synthetic-owner", password))?.UserId == ownerIdentity, "password login failed after restart");
            var logs = await http.GetStringAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/logs?num_lines=200", deadline.Token);
            Assert(!logs.Contains(password) && !logs.Contains(stored.Token), "provisioning logs exposed credentials");
            await VerifyHostedStartupAsync(owner, ownerIdentity, ownerToken, directory, deadline.Token);
            Console.WriteLine("PASS first-connection API identity, private persistence, owner-only atomic provisioning, existing-account/password preservation, actual password login/rejection, restart and diagnostic redaction");
        }
        finally
        {
            deadline.Cancel(); try { await pump; } catch (OperationCanceledException) { }
            api.Disconnect();
            if (apiIdentity.HasValue) await owner.ExecuteReducerAsync(new Reducer.RevokeTrustedApi(apiIdentity.Value), _ => true);
            Directory.Delete(directory, true);
        }
    }
    private static async Task VerifyHostedStartupAsync(SpacetimeDBService owner, Identity ownerIdentity, string ownerToken, string directory, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var body = new StringContent("[]", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var reset = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_bootstrap_test", body, ct);
        reset.EnsureSuccessStatusCode();
        var password = "synthetic-hosted-" + Guid.NewGuid().ToString("N");
        var ownerPath = Path.Combine(directory, "owner-input");
        var passwordPath = Path.Combine(directory, "password-input");
        void PrivateFile(string path, string value)
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, options);
            using var writer = new StreamWriter(file); writer.Write(value);
        }
        PrivateFile(ownerPath, ownerToken); PrivateFile(passwordPath, password);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SpacetimeDB:Host"] = "http://127.0.0.1:3000", ["SpacetimeDB:DatabaseName"] = "bru-architecture-integration",
            ["SpacetimeDB:CredentialFile"] = Path.Combine(directory, "host-api.json"), ["SpacetimeDB:LogReducerLogsToFile"] = "false",
            ["SpacetimeDB:Bootstrap:Enabled"] = "true", ["SpacetimeDB:Bootstrap:OwnerTokenFile"] = ownerPath,
            ["SpacetimeDB:Bootstrap:InitialAdminPasswordFile"] = passwordPath, ["SpacetimeDB:Bootstrap:AdminLogin"] = "synthetic-owner"
        }).Build();
        var api = new SpacetimeDBService(config, NullLogger<SpacetimeDBService>.Instance);
        using var coordinator = new BRU_AVTOPARK_AspireAPI.ApiService.Services.SpacetimeFrameTickService(api,
            NullLogger<BRU_AVTOPARK_AspireAPI.ApiService.Services.SpacetimeFrameTickService>.Instance);
        using var startup = new BRU_AVTOPARK_AspireAPI.ApiService.Services.ApiDatabaseStartup(api, config,
            NullLogger<BRU_AVTOPARK_AspireAPI.ApiService.Services.ApiDatabaseStartup>.Instance);
        var health = new BRU_AVTOPARK_AspireAPI.ApiService.Services.ApiDatabaseStartupHealthCheck(startup);
        Assert((await health.CheckHealthAsync(new())).Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, "API declared readiness before first connection");
        await coordinator.StartAsync(ct); await startup.StartAsync(ct);
        try
        {
            await Until(() => startup.IsReady, ct);
            var identity = api.GetLocalIdentity()!.Value;
            Assert(identity != ownerIdentity, "hosted API used owner identity");
            var authentication = new AuthenticationService(api, new RoleService(api, NullLogger<RoleService>.Instance), NullLogger<AuthenticationService>.Instance);
            Assert((await authentication.AuthenticateAsync("synthetic-owner", password))?.UserId == ownerIdentity, "API-owned startup did not initialize admin password");
            Assert((await health.CheckHealthAsync(new())).Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, "provisioned API readiness not healthy");
            await owner.ExecuteReducerAsync(new Reducer.RevokeTrustedApi(identity), _ => true);
            await Until(() => !startup.IsReady, ct);
            await Task.Delay(1500, ct);
            Assert(!await owner.ReadAsync(c => c.Db.TrustedApiIdentity.Identity.Find(identity) != null, ct), "startup silently regranted revoked API trust");
            Assert((await health.CheckHealthAsync(new())).Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, "revoked API still reported healthy");
            Console.WriteLine("PASS actual API hosted coordinator: first connection, owner-authorized automatic password/trust provisioning, readiness, admin login and no automatic regrant after revocation");
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None); await coordinator.StopAsync(CancellationToken.None);
        }
    }
    private static async Task Until(Func<bool> test, CancellationToken ct) { while (!test()) await Task.Delay(10, ct); }
    private static async Task UntilAsync(Func<Task<bool>> test, CancellationToken ct) { while (!await test()) await Task.Delay(10, ct); }
    private static async Task Reject(Func<Task> run) { try { await run(); } catch (ReducerRejectedException) { return; } throw new Exception("Unauthorized or invalid bootstrap succeeded."); }
    private static void RejectFile(Action run) { try { run(); } catch (IOException) { return; } throw new Exception("Credential scope/identity was not enforced."); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
