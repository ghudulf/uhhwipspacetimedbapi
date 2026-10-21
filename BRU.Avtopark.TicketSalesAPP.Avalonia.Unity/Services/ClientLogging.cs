using System;
using System.IO;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using Serilog;
using Serilog.Events;
using TicketSalesApp.Services.Diagnostics;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class ClientLogging
{
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BRU.Avtopark.TicketSalesApp", "logs");
    public static void Configure(string prefix = "app")
    {
        var options = ClientTrafficOptions.Current;
        TicketSalesApp.Services.Implementations.OwnerOnlyFile.CreateDirectory(LogDirectory);
        var unsafeTraffic = options.Level == TrafficLevel.Full;
        var formatter = new EscapedTextFormatter(unsafeTraffic);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .WriteTo.Console(formatter).WriteTo.Debug(formatter)
            .WriteTo.File(formatter, Path.Combine(LogDirectory, prefix + "-.log"), rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, retainedFileCountLimit: 14)
            .WriteTo.File(new TrafficJsonFormatter(unsafeTraffic), Path.Combine(LogDirectory, prefix + "-.jsonl"), rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, retainedFileCountLimit: 14)
            .CreateLogger();
        if (unsafeTraffic) Log.Warning(EscapedTextFormatter.UnsafeWarning);
        Log.Information("Client logs: {LogDirectory}; debug traffic level {TrafficLevel}", LogDirectory, options.Level);
    }
}
