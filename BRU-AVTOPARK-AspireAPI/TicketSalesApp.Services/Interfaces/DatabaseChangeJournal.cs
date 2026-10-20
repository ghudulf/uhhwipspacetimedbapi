namespace TicketSalesApp.Services.Interfaces;

public sealed record DatabaseWatermark(string Epoch, long Sequence);
public sealed record DatabaseChange(long Sequence, string Resource);
public sealed record DatabaseChangeBatch(DatabaseWatermark Watermark, bool Gap, DatabaseChange[] Changes);

/// <summary>Bounded invalidation history. Contains resource names only, never rows or authentication material.</summary>
public sealed class DatabaseChangeJournal(int capacity = 2048)
{
    private readonly object _gate = new();
    private readonly Queue<DatabaseChange> _changes = new();
    private string _epoch = Guid.NewGuid().ToString("N");
    private long _sequence;
    public DatabaseWatermark Current { get { lock (_gate) return new(_epoch, _sequence); } }
    public void Reset()
    {
        lock (_gate) { _epoch = Guid.NewGuid().ToString("N"); _sequence = 0; _changes.Clear(); }
    }
    public void Publish(string resource)
    {
        lock (_gate)
        {
            _changes.Enqueue(new(++_sequence, resource));
            while (_changes.Count > Math.Max(1, capacity)) _changes.Dequeue();
        }
    }
    public DatabaseChangeBatch Read(DatabaseWatermark after)
    {
        lock (_gate)
        {
            var gap = after.Epoch != _epoch || after.Sequence < 0 || after.Sequence > _sequence ||
                (_changes.Count > 0 && after.Sequence < _changes.Peek().Sequence - 1);
            return new(new(_epoch, _sequence), gap, gap ? [] : _changes.Where(c => c.Sequence > after.Sequence).ToArray());
        }
    }
}
