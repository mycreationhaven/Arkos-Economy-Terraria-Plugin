using System.Net;
using Newtonsoft.Json.Linq;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Integrations;

public sealed class ArkoviaNodeClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Func<EconomyConfig> _config;

    public ArkoviaNodeClient(Func<EconomyConfig> config) => _config = config;

    public async Task<int> GetHeightAsync(CancellationToken ct)
    {
        var root = await GetAsync(new Dictionary<string,string> { ["requestType"] = "getBlockchainStatus" }, ct);
        return root.Value<int?>("numberOfBlocks") is int blocks ? Math.Max(0, blocks - 1) : root.Value<int>("height");
    }

    public async Task<IReadOnlyList<BlockchainFundingEntry>> GetTreasuryLedgerAsync(CancellationToken ct)
    {
        var cfg = _config().Arkovia;
        var root = await GetAsync(new Dictionary<string,string>
        {
            ["requestType"] = "getAccountLedger",
            ["account"] = cfg.CommunityDevelopmentAccount,
            ["eventType"] = cfg.ExpectedLedgerEventType,
            ["holdingType"] = "NXT_BALANCE",
            ["firstIndex"] = "0",
            ["lastIndex"] = (cfg.LedgerPageSize - 1).ToString()
        }, ct);

        if (root["entries"] is not JArray entries) return Array.Empty<BlockchainFundingEntry>();
        var list = new List<BlockchainFundingEntry>();
        foreach (var token in entries.OfType<JObject>())
        {
            var eventType = token.Value<string>("eventType") ?? "";
            var eventId = token.Value<string>("event") ?? "";
            var block = token.Value<string>("block") ?? eventId;
            var height = token.Value<int?>("height") ?? 0;
            var timestamp = token.Value<int?>("timestamp") ?? 0;
            var change = ParseLong(token["change"]);
            var balance = ParseLong(token["balance"]);
            // ledgerId is peer-local and can change after rollback; block + event + account + change is deterministic enough for confirmed entries.
            var externalKey = $"arkovia:{cfg.CommunityDevelopmentAccount}:{block}:{eventId}:{change}";
            list.Add(new BlockchainFundingEntry(externalKey, eventId, block, height, timestamp, change, balance, eventType));
        }
        return list;
    }

    public async Task<long> GetAccountBalanceAtomicAsync(CancellationToken ct)
    {
        var cfg = _config().Arkovia;
        var root = await GetAsync(new Dictionary<string,string> { ["requestType"]="getBalance", ["account"]=cfg.CommunityDevelopmentAccount }, ct);
        return ParseLong(root["balanceNQT"] ?? root["balanceATM"] ?? root["balance"]);
    }

    private async Task<JObject> GetAsync(Dictionary<string,string> parameters, CancellationToken ct)
    {
        var cfg = _config().Arkovia;
        ValidateNodeUrl(cfg.NodeUrl, cfg.RequireNodeToBeLocalOrHttps);
        var query = string.Join("&", parameters.Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));
        var url = cfg.NodeUrl + (cfg.NodeUrl.Contains('?') ? "&" : "?") + query;
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        var root = JObject.Parse(text);
        if (root["errorCode"] is not null) throw new InvalidOperationException($"Arkovia node error {root["errorCode"]}: {root["errorDescription"]}");
        return root;
    }

    public static void ValidateNodeUrl(string url, bool requireLocalOrHttps)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new InvalidOperationException("Arkovia NodeUrl is invalid.");
        if (!requireLocalOrHttps) return;
        var local = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!local && uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Remote Arkovia node URLs must use HTTPS. Use localhost for a local node.");
    }

    private static long ParseLong(JToken? token) => long.TryParse(token?.ToString(), out var v) ? v : 0;
    public void Dispose() => _http.Dispose();
}
