using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public partial class SpacetimeDBService
{
    private const int QueueCapacity = 256;
    private const int OperationsPerFrame = 32;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);
    private readonly object _frameGate = new();
    private readonly List<Action> _connectionUnsubscriptions = new();
    private readonly Channel<QueuedWork> _operations = Channel.CreateBounded<QueuedWork>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false
    });
    private readonly ConcurrentDictionary<Reducer, PendingReducer> _pending = new(ReferenceEqualityComparer.Instance);
    private volatile DatabaseConnectionState _state;
    private volatile bool _eventsApplied;
    public DatabaseConnectionState State => _state;

    private sealed class QueuedWork(Action<DbConnection> run, Action<Exception> fail)
    {
        public readonly object Gate = new();
        public bool Abandoned;
        public bool Submitted;
        public Action<DbConnection> Run { get; } = run;
        public Action<Exception> Fail { get; } = fail;
    }
    private sealed record PendingReducer(Action<ReducerEventContext> Complete, Action<Exception> Fail, string Operation);

    public async Task<TResult> ExecuteReducerAsync<TArgs, TResult>(TArgs args,
        Func<DbConnection, TResult> committedResult, CancellationToken cancellationToken = default)
        where TArgs : Reducer, IReducerArgs, new()
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(committedResult);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = ((IReducerArgs)args).ReducerName;
        var correlation = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var started = Stopwatch.GetTimestamp();
        var work = new QueuedWork(conn =>
        {
            _pending.TryAdd(args, new PendingReducer(ctx =>
            {
                // SDK 2.1.0 applies this call's cache changes before dispatching its own-reducer callback.
                if (ctx.Event.Status is Status.Committed)
                {
                    try { completion.TrySetResult(DatabaseSnapshots.Detach(committedResult(conn))); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    _logger.LogInformation("Reducer committed {Operation} {CorrelationId} in {ElapsedMs}ms", operation, correlation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                else
                {
                    // Reducer error text can contain credentials from legacy reducers. Do not echo it.
                    _logger.LogWarning("Reducer rejected {Operation} {CorrelationId} status {Status}", operation, correlation, ctx.Event.Status.GetType().Name);
                    completion.TrySetException(new ReducerRejectedException(operation));
                }
            }, ex => completion.TrySetException(ex), operation));
            ((IDbConnection)conn).InternalCallReducer(args);
            _logger.LogDebug("Reducer submitted {Operation} {CorrelationId}; pending {PendingCount}", operation, correlation, _pending.Count);
        }, ex => completion.TrySetException(ex));
        Enqueue(work);
        try { return await completion.Task.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            lock (work.Gate)
            {
                work.Abandoned = true;
                _pending.TryRemove(args, out _);
                if (work.Submitted)
                {
                    _logger.LogWarning("Reducer outcome unknown {Operation} {CorrelationId}; {Reason}", operation, correlation, ex.GetType().Name);
                    throw new CommitOutcomeUnknownException(operation, ex);
                }
            }
            _logger.LogInformation("Reducer cancelled before submission {Operation} {CorrelationId}; {Reason}", operation, correlation, ex.GetType().Name);
            throw;
        }
        finally { _pending.TryRemove(args, out _); }
    }

    public async Task<TResult> ReadAsync<TResult>(Func<DbConnection, TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new QueuedWork(conn => completion.TrySetResult(DatabaseSnapshots.Detach(query(conn))), ex => completion.TrySetException(ex));
        Enqueue(work);
        try { return await completion.Task.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false); }
        finally { lock (work.Gate) work.Abandoned = true; }
    }

    private void Enqueue(QueuedWork work)
    {
        lock (_frameGate)
        {
            if (!IsSubscriptionReady()) throw new DatabaseUnavailableException("Database subscriptions are not ready.");
            if (!_operations.Writer.TryWrite(work)) throw new DatabaseUnavailableException("Database operation queue is full.");
        }
    }

    private void QueueLegacyCommand(string command, Dictionary<string, object> args)
    {
        var snapshot = new Dictionary<string, object>(args);
        Enqueue(new QueuedWork(_ => ProcessCommand(command, snapshot),
            ex => _logger.LogWarning("Legacy command {Command} was not completed: {Failure}", command, ex.GetType().Name)));
    }

    private void ProcessQueuedOperations()
    {
        if (!IsSubscriptionReady() || _connection == null) return;
        for (var i = 0; i < OperationsPerFrame && _operations.Reader.TryRead(out var work); i++)
        {
            lock (work.Gate)
            {
                if (work.Abandoned) continue;
                try
                {
                    work.Submitted = true;
                    work.Run(_connection);
                }
                catch (Exception ex) { work.Fail(ex); }
            }
        }
    }

    private void OnReducerCompleted(ReducerEventContext context)
    {
        // Reference identity is preserved by the SDK's pendingReducerCalls map. Equal argument
        // values on simultaneous calls cannot complete one another's task.
        if (_pending.TryRemove(context.Event.Reducer, out var pending)) pending.Complete(context);
        else if (context.Event.Status is not Status.Committed)
            _logger.LogWarning("Legacy reducer rejected: {Reducer}, status {Status}", context.Event.Reducer.GetType().Name, context.Event.Status.GetType().Name);
    }

    private void SetState(DatabaseConnectionState state)
    {
        if (_state == state) return;
        if (_state == DatabaseConnectionState.Ready) Changes.Reset();
        var previousPhase = _state; var nextPhase = state;
        _logger.LogInformation("Database phase {PreviousPhase} -> {NextPhase}; pending {PendingCount}", previousPhase, nextPhase, _pending.Count);
        _state = state;
    }

    private void UpdateReadiness()
    {
        if (_subscriptionApplied && _eventsApplied) SetState(DatabaseConnectionState.Ready);
    }

    private void InvalidateReadiness()
    {
        _subscriptionApplied = false;
        _eventsApplied = false;
        SetState(DatabaseConnectionState.Disconnected);
        foreach (var entry in _pending)
            if (_pending.TryRemove(entry.Key, out var pending))
                pending.Fail(new CommitOutcomeUnknownException(pending.Operation));
        while (_operations.Reader.TryRead(out var work))
            lock (work.Gate)
            {
                work.Abandoned = true;
                work.Fail(new DatabaseUnavailableException("Database disconnected before submission."));
            }
    }
}
