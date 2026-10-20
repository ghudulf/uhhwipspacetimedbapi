using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

internal static class ApplicationMutationClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<MutationResult> ExecuteAsync<T>(string operation, T payload, string operationId,
        string expectedBaseUrl, string expectedToken, CancellationToken cancellationToken)
    {
        var api = ApiClientService.Instance;
        if (api.AuthToken != expectedToken || api.CurrentBaseUrl != expectedBaseUrl)
            throw new ApplicationCommandException("session_changed");
        try
        {
            if (Environment.GetEnvironmentVariable("BRU_TRANSPORT") == "http") throw new OperationNotDispatchedException(operation);
            return await ApplicationClientCoordinator.ExecuteAsync<T, MutationResult>(expectedBaseUrl, expectedToken,
                operation, payload, operationId, cancellationToken) ?? throw new ApplicationCommandException("commit_outcome_unknown");
        }
        catch (OperationNotDispatchedException) { /* The command was not sent. It is safe to choose HTTP once. */ }
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(expectedBaseUrl), "operations/" + operation));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expectedToken);
        request.Headers.Add("Idempotency-Key", operationId);
        request.Content = JsonContent.Create(payload, options: ApiProtocol.Json);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new ApplicationCommandException("unauthenticated");
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                throw new ApplicationCommandException(document.RootElement.TryGetProperty("code", out var code)
                    ? code.GetString() ?? "commit_outcome_unknown" : "commit_outcome_unknown");
            }
            var result = await response.Content.ReadFromJsonAsync<MutationResult>(ApiProtocol.Json, cancellationToken)
                ?? throw new ApplicationCommandException("commit_outcome_unknown");
            if (api.AuthToken != expectedToken || api.CurrentBaseUrl != expectedBaseUrl) throw new ApplicationCommandException("session_changed");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // A transport failure after dispatch cannot prove that the server did not commit.
            throw new ApplicationCommandException("commit_outcome_unknown");
        }
    }
}
