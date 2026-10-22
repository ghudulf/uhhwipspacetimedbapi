using System.Text;
using System.Text.Json;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;

try
{
    if (args.FirstOrDefault() == "generate-secrets")
    {
        if (args.Length < 3 || args[1] != "--out" || args.Length > 4 || (args.Length == 4 && args[3] != "--force"))
            throw new BootstrapException("Usage: generate-secrets --out <dir> [--force]");
        var directory = Path.GetFullPath(args[2]);
        OwnerOnlyFile.CreateDirectory(directory);
        var path = Path.Combine(directory, "JwtSettings__Secret");
        if (new FileInfo(path).LinkTarget != null) throw new BootstrapException("Secret output must not be a symbolic link.");
        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var temporary = Path.Combine(directory, ".secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, fileOptions))
            {
                OwnerOnlyFile.Restrict(temporary);
                file.Write(Encoding.UTF8.GetBytes(secret));
                file.Flush(true);
            }
            File.Move(temporary, path, args.Contains("--force"));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Console.WriteLine("JwtSettings__Secret sha256:" + SecretConfiguration.Fingerprint(secret));
        return 0;
    }
    if (args.Contains("--help") || args.Length == 0)
    {
        Console.WriteLine("DatabaseBootstrap --host URL --database NAME --owner-token-file PATH [--credential-file PATH] [--admin-login admin] [--password-file PATH | --trust-only]");
        Console.WriteLine("DatabaseBootstrap generate-secrets --out <dir> [--force]");
        Console.WriteLine("Run after publishing the module. Connects as the API first, saves its generated identity, then provisions it with owner authorization. Password input is masked; existing passwords are never overwritten.");
        return 0;
    }
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    var trustOnly = false;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--trust-only") { trustOnly = true; continue; }
        if (args[i] is not ("--host" or "--database" or "--owner-token-file" or "--credential-file" or "--admin-login" or "--password-file") || i + 1 == args.Length)
            throw new BootstrapException("Unknown or incomplete option; use --help.");
        var key = args[i];
        if (!options.TryAdd(key, args[++i])) throw new BootstrapException("Duplicate option.");
    }
    string Required(string key) => options.GetValueOrDefault(key) ?? throw new BootstrapException("Required option: " + key);
    var host = DatabaseCredentialFile.NormalizeHost(Required("--host"));
    var uri = new Uri(host);
    var plaintextSetting = Environment.GetEnvironmentVariable("BRU_PLAINTEXT_POLICY");
    if (!BRU.Avtopark.Contracts.NetworkPolicy.IsValidMode(plaintextSetting)) throw new BootstrapException("BRU_PLAINTEXT_POLICY must be never, loopback, lan or any.");
    if (uri.Scheme != "https" && !BRU.Avtopark.Contracts.NetworkPolicy.IsPlaintextAllowed(uri, BRU.Avtopark.Contracts.NetworkPolicy.ParseMode(plaintextSetting)))
        throw new BootstrapException("Provisioning transport is blocked by BRU_PLAINTEXT_POLICY; use HTTPS.");
    var name = Required("--database");
    var ownerToken = ReadSecret(Required("--owner-token-file"));
    if (ownerToken.StartsWith('{'))
    {
        using var document = JsonDocument.Parse(ownerToken);
        ownerToken = document.RootElement.GetProperty("token").GetString() ?? throw new BootstrapException("Owner token file is empty.");
    }
    if (trustOnly && options.ContainsKey("--password-file")) throw new BootstrapException("Trust-only provisioning must not include a password file.");
    var login = options.GetValueOrDefault("--admin-login", "admin");
    var password = trustOnly ? null : options.TryGetValue("--password-file", out var passwordFile) ? ReadSecret(passwordFile) : PromptPassword();
    if (password != null && (password.Length < 12 || password.Length > 1024 || string.IsNullOrWhiteSpace(password)))
        throw new BootstrapException("Initial password must contain 12 to 1024 characters.");
    var credentialPath = options.GetValueOrDefault("--credential-file") ?? DatabaseCredentialFile.DefaultPath(host, name);
    var stored = DatabaseCredentialFile.Read(credentialPath, host, name);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; deadline.Cancel(); };
    var api = await Connect(host, name, stored?.Token, deadline.Token);
    try
    {
        DatabaseCredentialFile.Save(credentialPath, host, name, api.Identity.ToString(), api.Token);
        Console.WriteLine("API first connection completed; generated identity persisted: " + api.Identity);
        var owner = await Connect(host, name, ownerToken, deadline.Token);
        try
        {
            if (owner.Identity == api.Identity) throw new BootstrapException("The API must use a separate identity from the deployment owner. Choose a separate API credential file.");
            var provisioned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.Connection.Reducers.OnBootstrapInstallation += (context, _, _, _) => Complete(provisioned, context.Event.Status);
            owner.Connection.Reducers.BootstrapInstallation(api.Identity, password == null ? null : login, password);
            await Pump(owner.Connection, provisioned.Task, deadline.Token);
            Console.WriteLine("Owner-authorized provisioning committed.");
        }
        finally { owner.Connection.Disconnect(); }
        if (password != null)
        {
            var authenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Connection.Reducers.OnAuthenticateUserChecked += (context, _, _) => Complete(authenticated, context.Event.Status);
            api.Connection.Reducers.AuthenticateUserChecked(login, password);
            await Pump(api.Connection, authenticated.Task, deadline.Token);
            Console.WriteLine("Administrator password authentication verified through the provisioned API identity.");
        }
        Console.WriteLine("Start the API with SpacetimeDB__Host and SpacetimeDB__DatabaseName matching this command.");
        Console.WriteLine("SpacetimeDB__CredentialFile=" + Path.GetFullPath(credentialPath));
        Console.WriteLine("Keep this file private to the API service account. The owner token is not saved or required by the running API.");
    }
    finally { api.Connection.Disconnect(); }
    return 0;
}
catch (Exception exception)
{
    // SDK exception text may contain request data. Only our controlled errors are printed.
    Console.Error.WriteLine(exception is BootstrapException ? exception.Message : "Provisioning did not complete: " + exception.GetType().Name);
    Console.Error.WriteLine("No password is overwritten. For an existing credential use --trust-only. Check host/database, owner authorization and file permissions; retry with the same API credential file.");
    return 1;
}

static void Complete(TaskCompletionSource completion, Status status)
{
    if (status is Status.Committed) completion.TrySetResult();
    else completion.TrySetException(new BootstrapException("Database rejected provisioning/authentication. Check owner authority, active administrator and whether its initial password is already configured."));
}
static async Task Pump(DbConnection connection, Task completion, CancellationToken ct)
{
    while (!completion.IsCompleted) { ct.ThrowIfCancellationRequested(); connection.FrameTick(); await Task.Delay(10, ct); }
    await completion;
}
static async Task<(DbConnection Connection, Identity Identity, string Token)> Connect(string host, string name, string? token, CancellationToken ct)
{
    var connected = new TaskCompletionSource<(Identity, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
    var connection = DbConnection.Builder().WithUri(host).WithDatabaseName(name).WithToken(token)
        .OnConnect((_, identity, credential) => connected.TrySetResult((identity, credential)))
        .OnConnectError(_ => connected.TrySetException(new BootstrapException("Database connection failed; check endpoint and credential.")))
        .OnDisconnect((_, _) => connected.TrySetException(new BootstrapException("Database disconnected during bootstrap."))).Build();
    try { await Pump(connection, connected.Task, ct); var value = await connected.Task; return (connection, value.Item1, value.Item2); }
    catch { connection.Disconnect(); throw; }
}
static string ReadSecret(string path)
{
    if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
        throw new BootstrapException("Secret input files must be private (chmod 600).");
    if (new FileInfo(path).LinkTarget != null) throw new BootstrapException("Secret input file must not be a symbolic link.");
    return File.ReadAllText(path).TrimEnd('\r', '\n');
}
static string PromptPassword()
{
    if (Console.IsInputRedirected) throw new BootstrapException("Noninteractive use requires --password-file, never a password argument.");
    string Read(string prompt)
    {
        Console.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new BootstrapException("Provisioning cancelled.");
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar) && text.Length < 1024) text.Append(key.KeyChar);
        }
    }
    var password = Read("Initial admin password (12+ characters; input hidden): ");
    if (password != Read("Confirm password: ")) throw new BootstrapException("Passwords do not match.");
    return password;
}

internal sealed class BootstrapException(string message) : Exception(message);
