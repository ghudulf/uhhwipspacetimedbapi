namespace TicketSalesApp.Services.Interfaces;

public enum DatabaseConnectionState { Disconnected, Connecting, Subscribing, Ready, Degraded }

public sealed class DatabaseUnavailableException(string message) : Exception(message);
public sealed class ReducerRejectedException(string operation) : Exception($"Database rejected {operation}.");
/// <summary>The request was submitted, but its commit could not be confirmed. Never retry blindly.</summary>
public sealed class CommitOutcomeUnknownException(string operation, Exception? inner = null)
    : Exception($"Commit outcome for {operation} is unknown; reconcile before retrying.", inner);
