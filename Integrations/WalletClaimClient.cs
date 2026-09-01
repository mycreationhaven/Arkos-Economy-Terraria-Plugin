using System.Net.Http.Json;
using System.Text.Json;

namespace ArkoviaEconomy.Integrations;

public sealed class WalletClaimClient
{
    private readonly HttpClient _http;

    private const string ApiKeyFile =
        "/etc/arkovia-wallet-claim-api-key";

    private const string Endpoint =
        "http://127.0.0.1:4890/internal/claims";

    public WalletClaimClient()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<WalletClaimResult> CreateClaimAsync(
        int tshockUserId,
        string accountRS,
        string recoveryFile,
        CancellationToken ct)
    {
        var apiKey =
            await File.ReadAllTextAsync(
                ApiKeyFile,
                ct);

        apiKey =
            apiKey.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Wallet claim API key is unavailable.");
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                Endpoint);

        request.Headers.Add(
            "X-Arkovia-Claim-Key",
            apiKey);

        request.Content =
            JsonContent.Create(
                new
                {
                    TShockUserId = tshockUserId,
                    AccountRS = accountRS,
                    RecoveryFile = recoveryFile
                });

        using var response =
            await _http.SendAsync(
                request,
                ct);

        var body =
            await response.Content.ReadAsStringAsync(
                ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Wallet claim service returned HTTP {(int)response.StatusCode}.");
        }

        var result =
            JsonSerializer.Deserialize<WalletClaimResult>(
                body,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (result is null ||
            string.IsNullOrWhiteSpace(result.Code))
        {
            throw new InvalidOperationException(
                "Wallet claim service returned an invalid response.");
        }

        return result;
    }
}

public sealed record WalletClaimResult(
    string Code,
    string ExpiresUtc,
    int ExpiresInMinutes);
