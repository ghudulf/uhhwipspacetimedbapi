using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace TicketSalesApp.Services.Diagnostics;

/// <summary>JSONL counterpart of the readable sink, including one unsafe-mode warning per opened file.</summary>
public sealed class TrafficJsonFormatter(bool unsafeTraffic = false) : ITextFormatter
{
    private readonly JsonFormatter formatter = new(renderMessage: true);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextWriter, object> warned = new();
    private readonly object gate = new();
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (unsafeTraffic)
            lock (gate)
                if (!warned.TryGetValue(output, out _))
                {
                    warned.Add(output, new object());
                    formatter.Format(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Warning, null,
                        new MessageTemplateParser().Parse(EscapedTextFormatter.UnsafeWarning), []), output);
                }
        formatter.Format(logEvent, output);
    }
}
