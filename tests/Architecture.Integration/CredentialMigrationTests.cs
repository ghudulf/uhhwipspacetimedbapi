using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class CredentialMigrationTests
{
    private const string BaseUrl = "http://127.0.0.1:3000/v1/database/bru-architecture-integration";
    public static async Task RunAsync(SpacetimeDBService owner, Identity ownerIdentity, string ownerToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var http = new HttpClient();
        using var identityResponse = await http.PostAsync("http://127.0.0.1:3000/v1/identity", null, deadline.Token);
        identityResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(deadline.Token));
        var apiToken = document.RootElement.GetProperty("token").GetString()!;
        var api = new SpacetimeDBService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SpacetimeDB:Host"] = "http://127.0.0.1:3000", ["SpacetimeDB:DatabaseName"] = "bru-architecture-integration",
            ["SpacetimeDB:Token"] = apiToken, ["SpacetimeDB:LogReducerLogsToFile"] = "false"
        }).Build(), NullLogger<SpacetimeDBService>.Instance);
        api.Connect();
        var pump = Task.Run(async () => { while (!deadline.IsCancellationRequested) { api.ProcessFrameTick(); await Task.Delay(10, deadline.Token); } });
        Identity? apiIdentity = null;
        async Task Call(string reducer, params object?[] arguments)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/call/" + reducer);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            request.Content = new StringContent(JsonSerializer.Serialize(arguments), Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Owner fixture assertion failed: " + reducer + " HTTP " + (int)response.StatusCode + " (values omitted).");
            }
        }
        async Task AssertPrivate(Identity id, bool present, bool legacyEmpty, string? password = null) =>
            await Call("assert_credential_test_state", id.ToString(), present, legacyEmpty, password ?? "");
        async Task Reject(Func<Task> action)
        {
            try { await action(); } catch (ReducerRejectedException) { return; }
            throw new Exception("Non-owner migration unexpectedly succeeded.");
        }
        async Task DeniedQuery(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/sql");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent("SELECT * FROM UserCredential", Encoding.UTF8, "text/plain");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            using var response = await http.SendAsync(request, deadline.Token);
            var error = await response.Content.ReadAsStringAsync(deadline.Token);
            Check(!response.IsSuccessStatusCode && error.Contains("UserCredential", StringComparison.Ordinal) && error.Contains("private", StringComparison.OrdinalIgnoreCase), "non-owner private SQL query did not fail with a table-visibility denial");
        }
        async Task DeniedSubscription()
        {
            var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await api.ReadAsync(c => c.SubscriptionBuilder().OnApplied(_ => rejected.TrySetException(new Exception("Private credential subscription accepted.")))
                .OnError((_, _) => rejected.TrySetResult()).Subscribe(new[] { "SELECT * FROM UserCredential" }), deadline.Token);
            await rejected.Task.WaitAsync(deadline.Token);
        }
        try
        {
            while (!api.IsSubscriptionReady()) await Task.Delay(10, deadline.Token);
            apiIdentity = api.GetLocalIdentity()!.Value;
            await DeniedQuery(apiToken);
            await DeniedSubscription();
            await Reject(() => api.ExecuteReducerAsync(new Reducer.MigratePasswordCredentials(), _ => true));
            await owner.ExecuteReducerAsync(new Reducer.ProvisionTrustedApi(apiIdentity.Value), _ => true);
            while (!await api.ReadAsync(c => c.Db.UserProfile.Iter().Any(), deadline.Token)) await Task.Delay(10, deadline.Token);
            await DeniedQuery(apiToken);
            await DeniedSubscription();
            await Reject(() => api.ExecuteReducerAsync(new Reducer.MigratePasswordCredentials(), _ => true));
            Console.WriteLine("PASS private credential SQL and explicit subscriptions rejected for untrusted and trusted non-owner API; migration remains owner-only");

            Identity NewIdentity() => Identity.FromHexString(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            var legacy = NewIdentity(); var privateId = NewIdentity(); var claimOld = NewIdentity(); var claimNew = NewIdentity(); var registered = NewIdentity();
            var suffix = Guid.NewGuid().ToString("N");
            var legacyLogin = "synthetic-credential-legacy-" + suffix;
            var privateLogin = "synthetic-credential-private-" + suffix;
            var claimLogin = "synthetic-credential-claim-" + suffix;
            var registerLogin = "synthetic-credential-register-" + suffix;
            var password = "synthetic-" + Guid.NewGuid().ToString("N");
            await Call("prepare_credential_migration_test", legacy.ToString(), legacyLogin, password, false, false, true);
            await Call("prepare_credential_migration_test", privateId.ToString(), privateLogin, password, true, true, true);
            await Call("prepare_credential_migration_test", claimOld.ToString(), claimLogin, password, true, false, false);
            await AssertPrivate(legacy, false, false);
            var authentication = new AuthenticationService(api, new RoleService(api, NullLogger<RoleService>.Instance), NullLogger<AuthenticationService>.Instance);
            Check((await authentication.AuthenticateAsync(legacyLogin, password))?.UserId == legacy, "legacy fallback login failed before migration");
            Check((await authentication.AuthenticateAsync(privateLogin, password))?.UserId == privateId, "private credential not preferred to conflicting legacy hash");
            await owner.ExecuteReducerAsync(new Reducer.MigratePasswordCredentials(), _ => true);
            await AssertPrivate(legacy, true, true, password);
            await AssertPrivate(privateId, true, true, password);
            await owner.ExecuteReducerAsync(new Reducer.MigratePasswordCredentials(), _ => true);
            await AssertPrivate(legacy, true, true, password);
            await AssertPrivate(privateId, true, true, password);
            Check((await authentication.AuthenticateAsync(legacyLogin, password))?.UserId == legacy, "login failed after migration");
            Check((await authentication.AuthenticateAsync(privateLogin, password))?.UserId == privateId, "migration overwrote existing private credential");
            Check(await api.ReadAsync(c => c.Db.UserProfile.Iter().All(u => string.IsNullOrEmpty(u.PasswordHash))), "trusted API can still read public password hashes");
            Console.WriteLine("PASS pre/post-migration logins, private-first fallback, idempotent migration preserves existing credentials and clears every trusted API-visible profile hash");

            var changed = "changed-" + Guid.NewGuid().ToString("N");
            await Reject(() => api.ExecuteReducerAsync(new Reducer.ChangePassword(legacy, "wrong", changed, legacy), _ => true));
            await api.ExecuteReducerAsync(new Reducer.ChangePassword(legacy, password, changed, legacy), _ => true);
            await AssertPrivate(legacy, true, true, changed);
            Check((await authentication.AuthenticateAsync(legacyLogin, changed))?.UserId == legacy, "changed password login failed");
            Check(await authentication.AuthenticateAsync(legacyLogin, password) == null, "old password still authenticates");
            await api.ExecuteReducerAsync(new Reducer.RegisterUser(registerLogin, password, "", "", null, null, ownerIdentity, registered.ToString()), _ => true);
            await AssertPrivate(registered, true, true, password);
            Check((await authentication.AuthenticateAsync(registerLogin, password))?.UserId == registered, "registration login failed");
            await api.ExecuteReducerAsync(new Reducer.UpdateUser(registered, null, changed, null, null, null, null, ownerIdentity), _ => true);
            await AssertPrivate(registered, true, true, changed);
            await api.ExecuteReducerAsync(new Reducer.ClaimUserAccount(claimLogin, password, claimNew.ToString()), _ => true);
            await AssertPrivate(claimOld, false, true);
            await AssertPrivate(claimNew, true, true, password);
            Check((await authentication.AuthenticateAsync(claimLogin, password))?.UserId == claimNew, "identity re-key lost credential");
            Check(await api.ReadAsync(c => c.Db.UserProfile.Iter().All(u => string.IsNullOrEmpty(u.PasswordHash))), "password write exposed legacy hash");
            await DeniedQuery(apiToken);
            Console.WriteLine("PASS ChangePassword current-password verification, RegisterUser, administrator reset and ClaimUserAccount identity re-key write credentials only privately");
        }
        finally
        {
            deadline.Cancel(); try { await pump; } catch (OperationCanceledException) { }
            api.Disconnect();
            if (apiIdentity.HasValue) await owner.ExecuteReducerAsync(new Reducer.RevokeTrustedApi(apiIdentity.Value), _ => true);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
