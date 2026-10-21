using System.Text.Json;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

/// <summary>One-shot owner connection. Never replaces or persists the application's connection identity.</summary>
public static class DatabaseInstallationProvisioner
{
    public static string ReadPrivateSecret(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new IOException("Secret input must not be a symbolic link.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            throw new IOException("Secret input must be private (chmod 600).");
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }
    public static string ReadOwnerToken(string path)
    {
        var secret = ReadPrivateSecret(path);
        if (!secret.StartsWith('{')) return secret;
        using var document = JsonDocument.Parse(secret);
        return document.RootElement.GetProperty("token").GetString() ?? throw new IOException("Owner credential is missing.");
    }
    public static async Task ProvisionAsync(string host, string database, string ownerToken, Identity apiIdentity,
        string? login, string? password, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(DatabaseCredentialFile.NormalizeHost(host));
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new IOException("Remote database provisioning requires HTTPS.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var connected = new TaskCompletionSource<Identity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = DbConnection.Builder().WithUri(host).WithDatabaseName(database).WithToken(ownerToken)
            .OnConnect((_, identity, _) => connected.TrySetResult(identity))
            .OnConnectError(_ => connected.TrySetException(new DatabaseUnavailableException("Owner connection failed.")))
            .OnDisconnect((_, _) => { connected.TrySetException(new DatabaseUnavailableException("Owner connection lost.")); committed.TrySetException(new CommitOutcomeUnknownException("bootstrap_installation")); }).Build();
        void OnBootstrap(ReducerEventContext context, Identity identity, string? initialLogin, string? initialPassword)
        {
            if (context.Event.Status is Status.Committed) committed.TrySetResult();
            else committed.TrySetException(new ReducerRejectedException("bootstrap_installation"));
        }
        try
        {
            await PumpAsync(connected.Task);
            if (await connected.Task == apiIdentity) throw new IOException("The API must not run with its deployment owner's identity.");
            connection.Reducers.OnBootstrapInstallation += OnBootstrap;
            connection.Reducers.BootstrapInstallation(apiIdentity, login, password);
            await PumpAsync(committed.Task);
        }
        finally
        {
            connection.Reducers.OnBootstrapInstallation -= OnBootstrap;
            connection.Disconnect();
            // A failed connection can fault both completions; observe the one not awaited.
            _ = connected.Task.Exception; _ = committed.Task.Exception;
        }
        async Task PumpAsync(Task completion)
        {
            while (!completion.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                connection.FrameTick();
                await Task.Delay(10, deadline.Token);
            }
            await completion;
        }
    }
}
