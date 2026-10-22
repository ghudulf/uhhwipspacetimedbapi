using Serilog.Events;
using Serilog.Formatting;

namespace TicketSalesApp.Services.Diagnostics;

/// <summary>Readable one-line records; untrusted messages/properties/exceptions cannot forge another record.</summary>
public sealed class EscapedTextFormatter(bool unsafeTraffic = false) : ITextFormatter
{
    public const string UnsafeWarning = "unsafe debug traffic logging enabled";
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextWriter, object> warned = new();
    private readonly object gate = new();
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (unsafeTraffic)
        {
            lock (gate)
                if (!warned.TryGetValue(output, out _))
                { warned.Add(output, new object()); output.WriteLine("WARNING: " + UnsafeWarning); }
        }
        string Property(string name) => logEvent.Properties.TryGetValue(name, out var value) ? value.ToString().Trim('"') : "-";
        var message = $"{logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{logEvent.Level}] {Property("SourceContext")} TraceId={Property("TraceId")} {logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)}";
        if (logEvent.Exception != null) message += " " + logEvent.Exception;
        output.WriteLine(Escape(message));
    }
    public static string Escape(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\u001b", "\\u001b");
}
