using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Integrations;

public sealed class ArkoviaNodeClient : IDisposable
{
    private readonly HttpClient _http;

    private readonly Func<EconomyConfig> _config;

    public ArkoviaNodeClient(Func<EconomyConfig> config, HttpMessageHandler? handler = null)
    {
        _config = config;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task ValidateCurrencyAsync(CancellationToken ct)
    {
        var cfg = _config();
        if (cfg.CurrencyId.Length == 0)
        {
            cfg.BlockchainDecimals = 8;
            return;
        }
        var root = await GetAsync(new Dictionary<string, string>
        {
            ["requestType"] = "getCurrency", ["currency"] = cfg.CurrencyId
        }, ct);
        var name = root.Value<string>("name");
        var code = root.Value<string>("code");
        var decimals = root.Value<int?>("decimals");
        if (root.Value<string>("currency") != cfg.CurrencyId ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code) ||
            decimals is null or < 0 or > 8)
            throw new InvalidOperationException("Node returned invalid currency metadata. Economy startup cancelled.");
        cfg.CurrencyName = name;
        cfg.CurrencySymbol = code;
        cfg.BlockchainDecimals = decimals.Value;
    }

    public async Task<int> GetHeightAsync(CancellationToken ct)
    {
        var root = await GetAsync(
            new Dictionary<string, string>
            {
                ["requestType"] = "getBlockchainStatus"
            },
            ct);

        return root.Value<int?>("numberOfBlocks") is int blocks
            ? Math.Max(0, blocks - 1)
            : root.Value<int>("height");
    }

    public async Task<IReadOnlyList<BlockchainFundingEntry>>
        GetTreasuryLedgerAsync(CancellationToken ct)
    {
        var currencyId = _config().CurrencyId;
        var cfg = _config().Arkovia;
        var parameters = new Dictionary<string, string>
        {
            ["requestType"] = "getAccountLedger",
            ["account"] = cfg.CommunityDevelopmentAccount,
            ["eventType"] = _config().FundingEventType,
            ["holdingType"] = currencyId.Length == 0 ? "NXT_BALANCE" : "CURRENCY_BALANCE",
            ["firstIndex"] = "0",
            ["lastIndex"] = (cfg.LedgerPageSize - 1).ToString()
        };
        if (currencyId.Length > 0) parameters["holding"] = currencyId;
        var root = await GetAsync(parameters, ct);

        if (root["entries"] is not JArray entries)
            return Array.Empty<BlockchainFundingEntry>();

        var list = new List<BlockchainFundingEntry>();

        foreach (var token in entries.OfType<JObject>())
        {
            // Never trust a node to honor holding filters before crediting money.
            var expectedHolding = currencyId.Length == 0 ? "NXT_BALANCE" : "CURRENCY_BALANCE";
            if (token.Value<string>("holdingType") != expectedHolding ||
                (currencyId.Length > 0 && token.Value<string>("holding") != currencyId))
                continue;
            var eventType = token.Value<string>("eventType") ?? "";
            var eventId = token.Value<string>("event") ?? "";
            var block = token.Value<string>("block") ?? eventId;
            var height = token.Value<int?>("height") ?? 0;
            var timestamp = token.Value<int?>("timestamp") ?? 0;
            var change = ParseLong(token["change"]);
            var balance = ParseLong(token["balance"]);

            var externalKey =
                $"arkovia:{cfg.CommunityDevelopmentAccount}:{block}:{eventId}:{change}";

            if (currencyId.Length > 0)
                externalKey = $"currency:{currencyId}:" + externalKey;
            list.Add(new BlockchainFundingEntry(
                externalKey,
                eventId,
                block,
                height,
                timestamp,
                change,
                balance,
                eventType));
        }

        return list;
    }

    public Task<long> GetAccountBalanceAtomicAsync(CancellationToken ct)
        => GetAccountBalanceAtomicAsync(_config().Arkovia.CommunityDevelopmentAccount, ct);

    public async Task<long> GetAccountBalanceAtomicAsync(
        string account,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException(
                "Arkovia account is required.",
                nameof(account));

        try
        {
            var currencyId = _config().CurrencyId;
            if (currencyId.Length > 0)
            {
                var currency = await GetAsync(new Dictionary<string, string>
                {
                    ["requestType"] = "getAccountCurrencies",
                    ["account"] = account,
                    ["currency"] = currencyId
                }, ct);
                // Nxt returns an empty object when the account holds none of this currency.
                if (!currency.HasValues) return 0;
                if (currency.Value<string>("currency") != currencyId)
                    throw new InvalidOperationException("Node returned a different currency balance.");
                return ParseLong(currency["units"]);
            }
            var root = await GetAsync(
                new Dictionary<string, string>
                {
                    ["requestType"] = "getAccount",
                    ["account"] = account
                },
                ct);

            return ParseLong(
                root["balanceNQT"] ??
                root["balanceATM"] ??
                root["balance"]);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains(
                "Unknown account",
                StringComparison.OrdinalIgnoreCase))
        {
            // A newly generated Arkovia address may not exist
            // in blockchain state until its first confirmed transaction.
            return 0L;
        }
    }

    public async Task<GeneratedArkoviaWallet> GenerateWalletAsync(
        CancellationToken ct)
    {
        var secretPhrase = GenerateSecretPhrase();

        var root = await PostAsync(
            new Dictionary<string, string>
            {
                ["requestType"] = "getAccountId",
                ["secretPhrase"] = secretPhrase
            },
            ct);

        var accountId = root.Value<string>("account")
            ?? throw new InvalidOperationException(
                "Arkovia node did not return an account ID.");

        var accountRs = root.Value<string>("accountRS")
            ?? throw new InvalidOperationException(
                "Arkovia node did not return an ARK address.");

        var publicKey = root.Value<string>("publicKey")
            ?? throw new InvalidOperationException(
                "Arkovia node did not return a public key.");

        return new GeneratedArkoviaWallet(
            secretPhrase,
            accountId,
            accountRs,
            publicKey);
    }

    private async Task<JObject> GetAsync(
        Dictionary<string, string> parameters,
        CancellationToken ct)
    {
        var cfg = _config().Arkovia;

        ValidateNodeUrl(
            cfg.NodeUrl,
            cfg.RequireNodeToBeLocalOrHttps);

        var query = string.Join(
            "&",
            parameters.Select(kv =>
                $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));

        var url =
            cfg.NodeUrl +
            (cfg.NodeUrl.Contains('?') ? "&" : "?") +
            query;

        using var response = await _http.GetAsync(url, ct);

        response.EnsureSuccessStatusCode();

        var text =
            await response.Content.ReadAsStringAsync(ct);

        return ParseNodeResponse(text);
    }

    private async Task<JObject> PostAsync(
        Dictionary<string, string> parameters,
        CancellationToken ct)
    {
        var cfg = _config().Arkovia;

        ValidateNodeUrl(
            cfg.NodeUrl,
            cfg.RequireNodeToBeLocalOrHttps);

        using var content = new FormUrlEncodedContent(parameters);

        using var response =
            await _http.PostAsync(cfg.NodeUrl, content, ct);

        response.EnsureSuccessStatusCode();

        var text =
            await response.Content.ReadAsStringAsync(ct);

        return ParseNodeResponse(text);
    }

    private static JObject ParseNodeResponse(string text)
    {
        var root = JObject.Parse(text);

        if (root["errorCode"] is not null)
        {
            var code = root["errorCode"]?.ToString() ?? "unknown";
            var description =
                root["errorDescription"]?.ToString()
                ?? "Unknown Arkovia node error.";

            throw new InvalidOperationException(
                $"Arkovia node error {code}: {description}");
        }

        return root;
    }

    private static string GenerateSecretPhrase()
    {
        const string alphabet =
            "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        const int length = 64;

        var chars = new char[length];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[
                RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }

    public static void ValidateNodeUrl(
        string url,
        bool requireLocalOrHttps)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            throw new InvalidOperationException(
                "Arkovia NodeUrl is invalid.");
        }

        if (!requireLocalOrHttps)
            return;

        var local =
            uri.IsLoopback ||
            uri.Host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase);

        if (!local &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Remote Arkovia node URLs must use HTTPS. " +
                "Use localhost for a local node.");
        }
    }

    private static long ParseLong(JToken? token)
        => long.TryParse(
            token?.ToString(),
            out var value)
            ? value
            : throw new InvalidOperationException("Node response contains a missing or invalid atomic amount.");

    public void Dispose()
        => _http.Dispose();
}
