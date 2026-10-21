using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class ProviderClientContext
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public static IBrowserLauncher Browser { get; set; } = new SystemBrowserLauncher();
    public static IProviderLoginApi CreateApi() => new ProviderLoginClient(Http, ApiClientService.Instance.CurrentBaseUrl ?? throw new ProviderLoginException("invalid_server"));
    public static IPasskeyManagementApi CreateManagementApi() => new ProviderLoginClient(Http, ApiClientService.Instance.CurrentBaseUrl ?? throw new ProviderLoginException("invalid_server"));
}

public sealed class SystemBrowserLauncher(bool allowDevelopmentLoopback = false) : IBrowserLauncher
{
    public Task OpenAsync(Uri url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = allowDevelopmentLoopback; // Compatibility only: the shared policy now controls HTTP browser launches.
        if (!url.IsAbsoluteUri || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !ClientNetworkPolicy.IsHttpAllowed(url)) throw new ProviderLoginException("unsafe_url");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            if (process == null) throw new ProviderLoginException("browser_unavailable");
            return Task.CompletedTask;
        }
        catch { throw new ProviderLoginException("browser_unavailable"); }
    }
}
