using BRU.Avtopark.Contracts;
using TicketSalesApp.Services.Interfaces;

internal static class SubscriptionTests
{
    public static void Run()
    {
        var journal = new DatabaseChangeJournal(2);
        var start = journal.Current;
        journal.Publish("buses");
        var first = journal.Read(start);
        Assert(!first.Gap && first.Changes is [{ Resource: "buses" }], "committed invalidation missing");
        journal.Publish("access");
        journal.Publish("sales");
        Assert(journal.Read(start).Gap, "history overflow did not require fresh snapshot");
        var retained = journal.Read(first.Watermark);
        Assert(!retained.Gap && retained.Changes.Length == 2, "retained boundary falsely detected a gap");
        Assert(journal.Read(new(retained.Watermark.Epoch, long.MaxValue)).Gap, "future watermark accepted");
        var beforeReset = journal.Current;
        journal.Reset();
        Assert(journal.Read(beforeReset).Gap, "reconnect retained old epoch");
        Assert(!journal.Read(journal.Current).Gap, "fresh snapshot cannot synchronize");
        Console.WriteLine("PASS bounded committed-change journal, retained boundary, overflow, invalid cursor and reconnect epoch");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
