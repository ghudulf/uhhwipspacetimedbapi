using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

// Only a separately provisioned synthetic localhost database is accepted by this harness.
var ownerPath = Environment.GetEnvironmentVariable("BRU_TEST_OWNER_FILE")
    ?? throw new InvalidOperationException("Set BRU_TEST_OWNER_FILE to the task-local synthetic owner credential file.");
using var ownerDocument = JsonDocument.Parse(await File.ReadAllTextAsync(ownerPath));
var ownerToken = ownerDocument.RootElement.GetProperty("token").GetString()!;
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["SpacetimeDB:Host"] = "http://127.0.0.1:3000",
    ["SpacetimeDB:DatabaseName"] = "bru-architecture-integration",
    ["SpacetimeDB:Token"] = ownerToken,
    ["SpacetimeDB:LogReducerLogsToFile"] = "false"
}).Build();
var database = new SpacetimeDBService(config, NullLogger<SpacetimeDBService>.Instance);
using var shutdown = new CancellationTokenSource();
database.Connect();
var pumping = true;
var pump = Task.Run(async () =>
{
    while (!shutdown.IsCancellationRequested)
    {
        if (Volatile.Read(ref pumping)) database.ProcessFrameTick();
        await Task.Delay(10, shutdown.Token);
    }
});
try
{
    await Ready(database);
    var owner = database.GetLocalIdentity()!.Value;
    await database.ExecuteReducerAsync(new Reducer.ProvisionTrustedApi(owner), _ => true);
    Pass("owner provisions trusted API identity");
    await database.ExecuteReducerAsync(new Reducer.EnsureDefaultPermissions(), _ => true);
    var grants = await database.ReadAsync(c => (c.Db.Permission.Iter().Count(), c.Db.RolePermission.Iter().Count()));
    await database.ExecuteReducerAsync(new Reducer.EnsureDefaultPermissions(), _ => true);
    Assert(await database.ReadAsync(c => (c.Db.Permission.Iter().Count(), c.Db.RolePermission.Iter().Count())) == grants,
        "default permission repair duplicated permissions or grants");
    var repairedAccess = await new TicketSalesApp.Services.Authorization.UserAccessService(database).GetAsync(owner);
    Assert(new[] { "jobs.view", "jobs.create", "jobs.edit", "jobs.delete", "schedules.view", "permissions.view" }.All(repairedAccess.Allows),
        "default administrator grants missing");
    Pass("owner-only default permission migration is idempotent and supplies indexed administrator grants");
    if (args.Contains("--bootstrap-only", StringComparer.Ordinal))
    {
        await BootstrapTests.RunAsync(database, owner, ownerToken);
        return;
    }
    if (args.Contains("--diagnostics-only", StringComparer.Ordinal))
    {
        var sentinel = "synthetic-password-" + Guid.NewGuid().ToString("N");
        await database.ExecuteReducerAsync(new Reducer.DebugVerifyPassword(sentinel, "invalid-hash-shape"), _ => true);
        using var diagnosticHttp = new HttpClient();
        diagnosticHttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        var logBody = await diagnosticHttp.GetStringAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/logs?num_lines=200");
        Assert(!logBody.Contains(sentinel, StringComparison.Ordinal), "database diagnostics exposed password input");
        Assert(logBody.Contains("Credential values omitted", StringComparison.Ordinal), "diagnostic operation log not observed");
        Pass("actual reducer diagnostics retain verification outcomes without the synthetic password");
        return;
    }


    if (args.Contains("--catalog-only", StringComparer.Ordinal))
    {
        await CatalogTests.RunAsync(database, owner, ownerToken);
        return;
    }

    if (args.Contains("--fleet-only", StringComparer.Ordinal))
    {
        await FleetTests.RunAsync(database, owner, ownerToken);
        return;
    }

    await CredentialMigrationTests.RunAsync(database, owner, ownerToken);
    await AdminOperationTests.RunAsync(database, owner, ownerToken);
    await QrDeviceTests.RunAsync(database, owner, ownerToken);
    var accessService = new TicketSalesApp.Services.Authorization.UserAccessService(database);
    var access = await accessService.GetAsync(owner);
    Assert(access.IsActive && access.IsAdministrator && access.Allows("sales.update") && !access.Allows("missing.permission"), "access snapshot disagrees with database");
    var externalSubject = "synthetic-" + Guid.NewGuid().ToString("N");
    await database.ExecuteReducerAsync(new Reducer.LinkExternalAccount("windows", "synthetic-realm", externalSubject, owner), _ => true);
    Assert(await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find("windows\nsynthetic-realm\n" + externalSubject)?.UserId == owner), "provisioned account link missing");
    await Reject<ReducerRejectedException>(() => database.ExecuteReducerAsync(new Reducer.LinkExternalAccount("windows", "synthetic-realm", externalSubject, owner), _ => true));
    await database.ExecuteReducerAsync(new Reducer.UnlinkExternalAccount("windows", "synthetic-realm", externalSubject), _ => true);
    Assert(await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find("windows\nsynthetic-realm\n" + externalSubject) == null), "revoked account link retained");
    Pass("external link provision, explicit reassignment rejection and revocation");
    await database.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("windows", "synthetic-realm", externalSubject, owner), _ => true);
    await Reject<ReducerRejectedException>(() => database.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("windows", "synthetic-realm", externalSubject, owner), _ => true));
    Assert(await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find("windows\nsynthetic-realm\n" + externalSubject)?.UserId == owner), "self-link not committed");
    Pass("trusted API self-link preserves application identity and rejects duplicate mapping");
    const string entraIssuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
    var otherUser = await database.ReadAsync(c => c.Db.UserProfile.Iter().First(user => user.UserId != owner && user.IsActive).UserId);
    await database.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("entra", entraIssuer, "own-entra", owner), _ => true);
    await database.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("entra", entraIssuer, "other-entra", otherUser), _ => true);
    Assert(await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find("entra\n" + entraIssuer + "\nown-entra")?.UserId == owner), "Entra self-link not committed");
    await database.ExecuteReducerAsync(new Reducer.UnlinkOwnExternalAccount("entra", owner), _ => true);
    Assert(await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find("entra\n" + entraIssuer + "\nown-entra") == null &&
        c.Db.ExternalAccountLink.Key.Find("entra\n" + entraIssuer + "\nother-entra")?.UserId == otherUser &&
        c.Db.ExternalAccountLink.Key.Find("windows\nsynthetic-realm\n" + externalSubject)?.UserId == owner), "own unlink touched another account/provider or failed to commit");
    await Reject<ReducerRejectedException>(() => database.ExecuteReducerAsync(new Reducer.UnlinkOwnExternalAccount("entra", owner), _ => true));
    Pass("Entra trusted self-link and committed own-only unlink preserve other account/provider links; absent link rejected");


    var factors = new TwoFactorService(database, NullLogger<TwoFactorService>.Instance);
    var challenge = await factors.CreateTempTokenAsync(owner, "login");
    var secondChallenge = await factors.CreateTempTokenAsync(owner, "login");
    Assert((await factors.ValidateTempTokenAsync(secondChallenge)).isValid && secondChallenge != challenge, "second challenge was not persisted independently");
    Assert((await factors.ValidateTempTokenAsync(challenge)).isValid, "committed challenge unavailable");
    var consumption = await Task.WhenAll(factors.MarkTokenAsUsedAsync(challenge), factors.MarkTokenAsUsedAsync(challenge));
    Assert(consumption.Count(result => result) == 1, "concurrent second-factor replay succeeded");
    Assert(!(await factors.ValidateTempTokenAsync(challenge)).isValid, "consumed challenge remains valid");
    Pass("current permissions, legacy update alias, committed second-factor creation and concurrent replay rejection");

    var store = new TokenStore(database, NullLogger<TokenStore>.Instance);
    var token = await store.InstantiateAsync(default);
    var stableId = token.Id;
    await store.SetReferenceIdAsync(token, "test-reference-" + Guid.NewGuid().ToString("N"), default);
    await store.SetPayloadAsync(token, "payload-A", default);
    await store.SetStatusAsync(token, "valid", default);
    await store.SetTypeAsync(token, "refresh_token", default);
    using (var json = JsonDocument.Parse("{\"pkce_test\":{\"method\":\"S256\"}}"))
        await store.SetPropertiesAsync(token, json.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value), default);
    await store.CreateAsync(token, default);
    var firstReference = token.ReferenceId!;
    await store.SetReferenceIdAsync(token, "test-reference-" + Guid.NewGuid().ToString("N"), default);
    await store.SetPayloadAsync(token, "payload-B", default); // Equal length; must be exact persistence.
    await store.UpdateAsync(token, default);
    Assert(await store.GetIdAsync(token, default) == stableId, "mutable reference changed stable ID");
    Assert(await store.FindByReferenceIdAsync(firstReference, default) == null, "old reference still resolves");
    var coldStore = new TokenStore(database, NullLogger<TokenStore>.Instance);
    var persisted = await coldStore.FindByReferenceIdAsync(token.ReferenceId!, default);
    Assert(persisted?.Id == stableId && persisted.Payload == "payload-B", "cold store lost mapping or equal-length payload update");
    Assert(persisted!.Properties["pkce_test"].GetProperty("method").GetString() == "S256", "properties did not survive document disposal/persistence");
    Pass("stable ID, rotated reference, cloned properties, equal-length update and cold store persistence");

    var stale = await coldStore.FindByIdAsync(stableId, default) ?? throw new Exception("Missing token");
    token.Status = "redeemed";
    await store.UpdateAsync(token, default);
    stale.Status = "redeemed";
    await Reject<OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(async () => await coldStore.UpdateAsync(stale, default));
    Pass("concurrent redemption rejects stale revision");

    database.Disconnect();
    Assert(!database.IsSubscriptionReady(), "disconnect retained readiness");
    database.Connect();
    await Ready(database);
    Assert((await coldStore.FindByIdAsync(stableId, default))?.Status == "redeemed", "reconnect lost durable token");
    Pass("disconnect invalidates readiness and reconnect reloads persisted token");

    var apps = new ApplicationStore(database, NullLogger<ApplicationStore>.Instance);
    var client = new OpenIddictApplication
    {
        ClientId = "synthetic-" + Guid.NewGuid().ToString("N"), Type = "public", ConsentType = "explicit",
        DisplayName = "Synthetic client", RedirectUris = ["https://client.invalid/callback"],
        PostLogoutRedirectUris = ["https://client.invalid/logout"], Permissions = ["scp:openid"]
    };
    client.Requirements = ["ft:pkce"];
    using (var properties = JsonDocument.Parse("{\"policy\":\"synthetic\"}"))
        await apps.SetPropertiesAsync(client, ImmutableDictionary<string, JsonElement>.Empty.Add("custom", properties.RootElement), default);
    await apps.CreateAsync(client, default);
    var coldApps = new ApplicationStore(database, NullLogger<ApplicationStore>.Instance);
    Assert((await coldApps.FindByClientIdAsync(client.ClientId, default))?.RedirectUris.Single() == client.RedirectUris.Single(), "client commit not durable");
    var logoutMatches = new List<OpenIddictApplication>();
    await foreach (var match in coldApps.FindByPostLogoutRedirectUriAsync(client.PostLogoutRedirectUris.Single(), default)) logoutMatches.Add(match);
    Assert(logoutMatches.Any(x => x.ClientId == client.ClientId), "post logout query used authorization redirect URIs");
    var staleClient = (await coldApps.FindByClientIdAsync(client.ClientId, default))!;
    Assert(staleClient.Requirements.Contains("ft:pkce") && staleClient.Properties["custom"].GetProperty("policy").GetString() == "synthetic", "application metadata lost");
    client.DisplayName = "Changed synthetic client";
    await apps.UpdateAsync(client, default);
    await Reject<OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(async () => await coldApps.UpdateAsync(staleClient, default));
    await apps.DeleteAsync(client, default);
    Assert(await coldApps.FindByClientIdAsync(client.ClientId, default) == null, "revoked client remained visible");
    var scopes = new ScopeStore(database, NullLogger<ScopeStore>.Instance);
    var scope = new OpenIddict.Abstractions.OpenIddictScopeDescriptor { Name = "synthetic-" + Guid.NewGuid().ToString("N"), Description = "Before replacement" };
    await scopes.SetResourcesAsync(scope, ["synthetic-resource"], default);
    using (var doc = JsonDocument.Parse("{\"nested\":true}"))
        await scopes.SetPropertiesAsync(scope, ImmutableDictionary<string, JsonElement>.Empty.Add("custom", doc.RootElement), default);
    await scopes.CreateAsync(scope, default);
    var scopeId = await scopes.GetIdAsync(scope, default);
    var coldScopes = new ScopeStore(database, NullLogger<ScopeStore>.Instance);
    var persistedScope = await coldScopes.FindByIdAsync(scopeId!, default);
    Assert(persistedScope!.Resources.Contains("synthetic-resource") && persistedScope.Properties["custom"].GetProperty("nested").GetBoolean(), "scope properties/resources lost on cold lookup");
    await Reject<ReducerRejectedException>(async () => await scopes.CreateAsync(new OpenIddict.Abstractions.OpenIddictScopeDescriptor { Name = scope.Name }, default));
    scope.Description = null;
    scope.Name += "-renamed";
    await scopes.UpdateAsync(scope, default);
    Assert(await scopes.GetIdAsync(scope, default) == scopeId, "renaming changed scope ID");
    Assert((await coldScopes.FindByIdAsync(scopeId!, default))!.Description == null, "scope null was not persisted");
    await Reject<OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(async () => await coldScopes.UpdateAsync(persistedScope, default));
    await scopes.DeleteAsync(scope, default);
    Assert(await coldScopes.FindByIdAsync(scopeId!, default) == null, "deleted scope remained visible");
    Pass("application revocation/logout matching and scope committed properties/resources, cold reads, duplicate rejection");

    // Remove prior synthetic sales after interrupted runs, retaining receipts and account IDs.
    var fixtureSales = new TicketSalesService(database, NullLogger<TicketSalesService>.Instance);
    foreach (var previousId in await database.ReadAsync(c => c.Db.Sale.Iter().Select(x => x.SaleId).ToArray()))
        await fixtureSales.DeleteSaleAsync(previousId, actingUser: owner);
    var ticket = await database.ReadAsync(c => c.Db.Ticket.Iter().First(t => t.IsActive && !c.Db.Sale.Iter().Any(s => s.TicketId == t.TicketId)));
    var sales = new TicketSalesService(database, NullLogger<TicketSalesService>.Instance);
    var operationId = Guid.NewGuid().ToString("N");
    var saleId = (await sales.CreateSaleAsync(ticket.TicketId, "Synthetic buyer", "", actingUser: owner, operationId: operationId))!.Value;
    var before = await database.ReadAsync(c => c.Db.Sale.SaleId.Find(saleId)!);
    Assert(before.SellerId == owner && before.TotalAmount == ticket.TicketPrice, "sale seller/amount not recorded");
    Assert(await sales.CreateSaleAsync(ticket.TicketId, "Synthetic buyer", "", actingUser: owner, operationId: operationId) == saleId, "idempotent create returned another sale");
    await Reject<ReducerRejectedException>(() => sales.CreateSaleAsync(ticket.TicketId, "Different payload", "", actingUser: owner, operationId: operationId));
    await Reject<ReducerRejectedException>(() => sales.CreateSaleAsync(ticket.TicketId, "Duplicate sale", "", actingUser: owner));
    using (var fixtureHttp = new HttpClient())
    {
        fixtureHttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        async Task SetPermissionAlias(bool legacy)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new[] { legacy }));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var result = await fixtureHttp.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/set_test_sales_permission_spelling", content);
            result.EnsureSuccessStatusCode();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await database.ReadAsync(c => c.Db.Permission.Iter().Any(p => p.Name == (legacy ? "sales.update" : "sales.edit")), limit.Token))
                await Task.Delay(10, limit.Token);
        }
        await SetPermissionAlias(true);
        try
        {
            var legacyAccess = await accessService.GetAsync(owner);
            Assert(legacyAccess.Allows("sales.edit") && legacyAccess.Allows("sales.update"), "gateway legacy permission mismatch");
            await sales.UpdateSaleAsync(saleId, "Edited buyer", actingUser: owner, expectedRevision: 1);
        }
        finally { await SetPermissionAlias(false); }
        Pass("legacy update permission grants remain effective through both gateway and reducer checks without changing grant IDs");
    }
    var after = await database.ReadAsync(c => c.Db.Sale.SaleId.Find(saleId)!);
    Assert(after.SaleId == before.SaleId && after.SaleDate == before.SaleDate && after.SellerId == before.SellerId && after.TotalAmount == before.TotalAmount, "edit replaced sale/history");
    await Reject<ReducerRejectedException>(() => sales.UpdateSaleAsync(saleId, "Stale edit", actingUser: owner, expectedRevision: 1));
    Pass("sale identity/history/amount preserved, duplicate/reused operation rejected, revisions enforced");

    using var http = new HttpClient();
    using var strangerResponse = await http.PostAsync("http://127.0.0.1:3000/v1/identity", null);
    strangerResponse.EnsureSuccessStatusCode();
    using var strangerDocument = JsonDocument.Parse(await strangerResponse.Content.ReadAsStringAsync());
    var strangerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SpacetimeDB:Host"] = "http://127.0.0.1:3000", ["SpacetimeDB:DatabaseName"] = "bru-architecture-integration",
        ["SpacetimeDB:Token"] = strangerDocument.RootElement.GetProperty("token").GetString()
    }).Build();
    var stranger = new SpacetimeDBService(strangerConfig, NullLogger<SpacetimeDBService>.Instance);
    stranger.Connect();
    using var strangerShutdown = new CancellationTokenSource();
    var strangerPump = Task.Run(async () => { while (!strangerShutdown.IsCancellationRequested) { stranger.ProcessFrameTick(); await Task.Delay(10, strangerShutdown.Token); } });
    try
    {
        await Ready(stranger);
        await QrDeviceTests.OutsiderAsync(stranger, owner);
        Assert(await stranger.ReadAsync(c => c.Db.UserProfile.Iter().Count()) == 0, "untrusted caller read password profiles");
        Assert(await stranger.ReadAsync(c => c.Db.OpenIddictSpacetimeToken.Iter().Count()) == 0, "untrusted caller read tokens");
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.LinkExternalAccount("windows", "synthetic-realm", "attacker", owner), _ => true));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("windows", "synthetic-realm", "attacker", owner), _ => true));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.UnlinkOwnExternalAccount("entra", owner), _ => true));
        Assert(!await stranger.ReadAsync(c => c.Db.ExternalAccountLink.Iter().Any()), "outsider can read external links");
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.ProvisionTrustedApi(stranger.GetLocalIdentity()!.Value), _ => true));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.EnsureDefaultPermissions(), _ => true));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.UpdateSale(saleId, null, "Forged actor", null, null, null, owner), _ => true));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.DeleteOidcToken(token.DatabaseId!.Value), _ => true));
        Pass("RLS blocks secret reads, untrusted provisioning/delegation/token mutations rejected");
        var strangerId = stranger.GetLocalIdentity()!.Value;
        await database.ExecuteReducerAsync(new Reducer.ProvisionTrustedApi(strangerId), _ => true);
        await Eventually(async () => await stranger.ReadAsync(c => c.Db.UserProfile.Iter().Any()));
        await database.ExecuteReducerAsync(new Reducer.RevokeTrustedApi(strangerId), _ => true);
        await Eventually(async () => await stranger.ReadAsync(c => !c.Db.UserProfile.Iter().Any() && !c.Db.OpenIddictSpacetimeToken.Iter().Any()));
        await Reject<ReducerRejectedException>(() => stranger.ExecuteReducerAsync(new Reducer.DeleteOidcToken(token.DatabaseId!.Value), _ => true));
        Pass("provisioned non-owner subscription receives private rows; revocation removes them and blocks writes");
    }
    finally { strangerShutdown.Cancel(); try { await strangerPump; } catch (OperationCanceledException) { } stranger.Disconnect(); }

    // Queued cancellation must not submit after the user has cancelled it.
    Volatile.Write(ref pumping, false);
    await Task.Delay(30);
    using var cancel = new CancellationTokenSource();
    var neverSubmitted = database.ExecuteReducerAsync(new Reducer.DeleteOidcToken(token.DatabaseId!.Value), _ => true, cancel.Token);
    cancel.Cancel();
    await Reject<OperationCanceledException>(() => neverSubmitted);
    Volatile.Write(ref pumping, true);
    Assert(await coldStore.FindByIdAsync(stableId, default) != null, "cancelled queued mutation was submitted");
    Pass("cancellation before dispatch does not mutate data");
    await store.DeleteAsync(token, default);
    Assert(await coldStore.FindByIdAsync(stableId, default) == null, "delete was not confirmed");
    await database.ExecuteReducerAsync(new Reducer.UnlinkExternalAccount("windows", "synthetic-realm", externalSubject), _ => true);
    Console.WriteLine("All architecture integration scenarios passed.");
}
finally
{
    shutdown.Cancel();
    try { await pump; } catch (OperationCanceledException) { }
    database.Disconnect();
}

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static void Pass(string name) => Console.WriteLine("PASS " + name);
static async Task Ready(ISpacetimeDBService database)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    while (!database.IsSubscriptionReady()) await Task.Delay(25, timeout.Token);
}
static async Task Reject<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); } catch (TException) { return; }
    throw new Exception("Expected " + typeof(TException).Name);
}

static async Task Eventually(Func<Task<bool>> check)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!await check()) await Task.Delay(25, timeout.Token);
}
