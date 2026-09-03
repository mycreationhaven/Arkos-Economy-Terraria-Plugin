using Newtonsoft.Json.Linq;

namespace ArkoviaEconomy.Integrations;

public sealed class ArkoviaNodeException(string code, string description)
    : InvalidOperationException($"Arkovia node error {code}: {description}")
{
    public string Code { get; } = code;
}
public sealed partial class ArkoviaNodeClient
{
    public Task<JObject> QueryAsync(string requestType, CancellationToken ct, params (string Key, string Value)[] values)
    {
        var args = values.ToDictionary(v => v.Key, v => v.Value); args["requestType"] = requestType;
        return GetAsync(args, ct);
    }
    public async Task<JObject?> TransactionAsync(string hash, CancellationToken ct)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidOperationException("Use the 64-character transaction full hash.");
        try { return await QueryAsync("getTransaction", ct, ("fullHash", hash)); }
        catch (ArkoviaNodeException ex) when (ex.Code == "5") { return null; }
    }
    public Task<JObject> ParseTransactionAsync(string bytes, CancellationToken ct) => PostAsync(new()
        { ["requestType"] = "parseTransaction", ["transactionBytes"] = bytes }, ct);
    public Task<JObject> BroadcastAsync(string bytes, CancellationToken ct) => PostAsync(new()
        { ["requestType"] = "broadcastTransaction", ["transactionBytes"] = bytes }, ct);
    public async Task<string> ResolveAccountAsync(string account, CancellationToken ct)
    {
        var result = await QueryAsync("getAccount", ct, ("account", account));
        return result.Value<string>("account") ?? throw new InvalidOperationException("Reserve/source account must exist on the node.");
    }
    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        var status = await QueryAsync("getBlockchainStatus", ct);
        if (status.Value<bool?>("isScanning") == true || status.Value<bool?>("isDownloading") == true)
            throw new InvalidOperationException("Node is synchronizing; try again later.");
        var last = status.Value<string>("lastBlock") ?? throw new InvalidOperationException("Node did not report a chain tip.");
        var block = await QueryAsync("getBlock", ct, ("block", last));
        var time = await QueryAsync("getTime", ct);
        if (time.Value<int?>("time") is not int now || block.Value<int?>("timestamp") is not int tip || now - tip > 600 || tip > now + 60)
            throw new InvalidOperationException("Node chain tip is stale; transfers are paused.");
    }
}
