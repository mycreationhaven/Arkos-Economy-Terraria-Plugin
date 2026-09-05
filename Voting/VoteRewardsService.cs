using System.Collections.Concurrent;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;

namespace ArkoviaEconomy.Voting;

public sealed class VoteRewardsService : IDisposable
{
    private readonly EconomyDatabase _db;
    private readonly EconomyService _economy;
    private readonly Func<EconomyConfig> _config;
    private readonly HttpClient _http = new();
    private readonly ConcurrentDictionary<int, DateTime> _cooldowns = new();
    private readonly ConcurrentDictionary<int, string> _tserverWebCaptcha = new();

    public VoteRewardsService(EconomyDatabase db, EconomyService economy, Func<EconomyConfig> config)
    {
        _db = db;
        _economy = economy;
        _config = config;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ArkoviaEconomy-VoteRewards/1.4");
    }

    public IEnumerable<Command> BuildCommands()
    {
        // TShock already uses /vote for its built-in vote/poll system. Registering another
        // /vote command makes dispatch order-dependent and can produce messages such as
        // "No active vote" instead of reaching Arkovia vote rewards.
        yield return new Command(Permissions.Vote, Vote, "arkvote", "voterewards")
        {
            AllowServer = false,
            HelpText = "/arkvote links|claim [provider]|status|debug or /arkvote tserverweb [captcha-answer]"
        };
    }

    private void Vote(CommandArgs args)
    {
        try
        {
            var cfg = _config().Voting;
            if (!cfg.Enabled) throw new InvalidOperationException("Vote rewards are disabled on this server.");
            if (!args.Player.RealPlayer || !args.Player.IsLoggedIn || args.Player.Account is null)
                throw new InvalidOperationException("You must be logged into a TShock account.");

            var action = args.Parameters.FirstOrDefault()?.ToLowerInvariant() ?? "links";
            if (action is "links" or "help") { ShowLinks(args.Player, cfg); return; }
            if (action == "status") { ShowStatus(args.Player, cfg); return; }
            if (action == "debug") { ShowDebug(args.Player, cfg); return; }

            // Network-backed actions run asynchronously, but all exceptions are observed
            // and reported by ProcessAsync instead of escaping an async-void callback.
            _ = ProcessAsync(args, cfg, action);
        }
        catch (Exception ex)
        {
            ReportError(args.Player, ex);
        }
    }

    private async Task ProcessAsync(CommandArgs args, VotingConfig cfg, string action)
    {
        try
        {
            if (action == "tserverweb")
            {
                var provider = cfg.Providers.FirstOrDefault(p => p.Enabled && p.Type.Equals("TServerWeb", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("TServerWeb voting is not configured.");
                EnsureCaps(args.Player, provider);
                await ProcessTServerWeb(args.Player, provider, args.Parameters.Skip(1).FirstOrDefault());
                return;
            }

            if (action != "claim")
                throw new InvalidOperationException("Use /arkvote links, /arkvote claim [provider], /arkvote status, /arkvote debug, or /arkvote tserverweb.");

            EnforceCooldown(args.Player.Account.ID, cfg);
            var requested = args.Parameters.Skip(1).FirstOrDefault()?.ToLowerInvariant();
            var providers = cfg.Providers.Where(p => p.Enabled && p.Type.Equals("TerrariaServers", StringComparison.OrdinalIgnoreCase) &&
                (requested is null || p.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))).ToList();
            if (providers.Count == 0) throw new InvalidOperationException("No matching claim-based voting provider is enabled.");
            foreach (var provider in providers)
            {
                EnsureCaps(args.Player, provider);
                await ClaimTerrariaServers(args.Player, provider);
            }
        }
        catch (Exception ex)
        {
            ReportError(args.Player, ex);
        }
    }

    private static void ReportError(TSPlayer player, Exception ex)
    {
        player.SendErrorMessage("[Vote Rewards] " + ex.Message);
        EconomyLog.Warn($"[VoteRewards] {ex.GetType().Name}: {ex.Message}");
    }

    private void ShowLinks(TSPlayer player, VotingConfig cfg)
    {
        player.SendInfoMessage("Vote for this server and earn configured rewards:");
        var enabled = cfg.Providers.Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
        {
            player.SendWarningMessage("No vote reward providers are currently enabled.");
            return;
        }
        foreach (var provider in enabled)
            player.SendInfoMessage($"{provider.DisplayName}: {(provider.VotingUrl.Length > 0 ? provider.VotingUrl : "ask an administrator for the voting link")}");
        player.SendInfoMessage("After voting on Terraria-Servers.com use /arkvote claim. For TServerWeb use /arkvote tserverweb.");
    }

    private void ShowStatus(TSPlayer player, VotingConfig cfg)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var total = _db.CountVoteClaims(player.Account.ID, day);
        player.SendInfoMessage($"Vote rewards claimed today: {total}/{cfg.MaximumRewardedVotesPerAccountPerDay} (UTC).");
        foreach (var provider in cfg.Providers.Where(p => p.Enabled))
            player.SendInfoMessage($"{provider.DisplayName}: {_db.CountVoteClaims(player.Account.ID, day, provider.Id)}/{provider.MaximumClaimsPerAccountPerDay}");
    }

    private static void ShowDebug(TSPlayer player, VotingConfig cfg)
    {
        var enabled = cfg.Providers.Where(p => p.Enabled).ToList();
        player.SendInfoMessage($"[Vote Rewards] enabled={cfg.Enabled}, providers={cfg.Providers.Count}, enabled providers={enabled.Count}.");
        if (enabled.Count == 0)
        {
            player.SendWarningMessage("[Vote Rewards] No enabled providers are configured.");
            return;
        }
        foreach (var provider in enabled)
            player.SendInfoMessage($"[Vote Rewards] id={provider.Id}, type={provider.Type}, max/day={provider.MaximumClaimsPerAccountPerDay}, voting URL={(string.IsNullOrWhiteSpace(provider.VotingUrl) ? "missing" : "set")}, API key={(string.IsNullOrWhiteSpace(provider.ApiKey) ? "missing" : "set")}, server ID={(string.IsNullOrWhiteSpace(provider.ServerId) ? "missing" : "set")}.");
    }

    private void EnforceCooldown(int userId, VotingConfig cfg)
    {
        var now = DateTime.UtcNow;
        if (_cooldowns.TryGetValue(userId, out var last) && (now - last).TotalSeconds < cfg.ClaimCooldownSeconds)
            throw new InvalidOperationException("Please wait before checking for another vote.");
        _cooldowns[userId] = now;
    }

    private async Task ClaimTerrariaServers(TSPlayer player, VoteProviderConfig provider)
    {
        var baseUrl = "https://terraria-servers.com/api/";
        var query = $"?object=votes&element=claim&key={Uri.EscapeDataString(provider.ApiKey)}&username={Uri.EscapeDataString(player.Account.Name)}";
        var status = await GetText(baseUrl + query, provider.TimeoutSeconds);
        if (status.Trim() == "0") { player.SendInfoMessage($"[{provider.DisplayName}] No unclaimed vote was found for your TShock account name."); return; }
        if (status.Trim() == "2") { player.SendInfoMessage($"[{provider.DisplayName}] Today's vote was already claimed."); return; }
        if (status.Trim() != "1") throw new InvalidOperationException($"{provider.DisplayName} returned an unexpected verification response.");

        var claim = await GetText(baseUrl + "?action=post&" + query.TrimStart('?'), provider.TimeoutSeconds);
        if (claim.Trim() != "1") throw new InvalidOperationException($"{provider.DisplayName} did not confirm the claim.");
        Award(player, provider);
    }

    private async Task ProcessTServerWeb(TSPlayer player, VoteProviderConfig provider, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) EnforceCooldown(player.Account.ID, _config().Voting);
        else if (!_tserverWebCaptcha.ContainsKey(player.Account.ID))
            throw new InvalidOperationException("Start the TServerWeb vote with /arkvote tserverweb before answering a CAPTCHA.");
        var url = "https://www.tserverweb.com/vote.php?user=" + Uri.EscapeDataString(player.Account.Name) +
                  "&sid=" + Uri.EscapeDataString(provider.ServerId);
        if (!string.IsNullOrWhiteSpace(answer)) url += "&answer=" + Uri.EscapeDataString(answer);
        var json = await GetText(url, provider.TimeoutSeconds, "TServerWeb Vote Plugin");
        var response = JsonConvert.DeserializeObject<TServerWebResponse>(json)
            ?? throw new InvalidOperationException("TServerWeb returned an invalid response.");
        switch (response.Response)
        {
            case "captcha":
                _tserverWebCaptcha[player.Account.ID] = response.Message;
                player.SendInfoMessage("[TServerWeb] CAPTCHA: " + response.Message);
                player.SendInfoMessage("Answer with /arkvote tserverweb <answer>");
                return;
            case "success" when !response.Message.Contains("wait 24 hours", StringComparison.OrdinalIgnoreCase):
                _tserverWebCaptcha.TryRemove(player.Account.ID, out _);
                Award(player, provider);
                return;
            case "success": player.SendInfoMessage("[TServerWeb] " + response.Message); return;
            default: throw new InvalidOperationException("TServerWeb: " + response.Message);
        }
    }

    private void Award(TSPlayer player, VoteProviderConfig provider)
    {
        var cfg = _config();
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        EnsureCaps(player, provider);

        var atomic = cfg.ToAtomic(provider.Rewards.CurrencyAmount);
        var nextClaimNumber = _db.CountVoteClaims(player.Account.ID, day, provider.Id) + 1;
        var claimKey = $"vote:{provider.Id}:{player.Account.ID}:{day}:{nextClaimNumber}";
        if (!_db.TryReserveVoteClaim(claimKey, player.Account.ID, player.Account.Name, provider.Id, day, atomic,
                JsonConvert.SerializeObject(provider.Rewards.Items), JsonConvert.SerializeObject(provider.Rewards.Groups)))
            throw new InvalidOperationException("This vote reward is already claimed or being processed.");
        var completed = false;
        try
        {
            if (atomic > 0)
            {
                var treasury = _economy.GetTreasury();
                var account = _economy.GetOrCreatePlayer(player.Account.ID, player.Account.Name);
                _economy.Transfer(treasury, account, atomic, "reward", "vote", claimKey,
                    $"Vote reward from {provider.DisplayName}", "VoteRewards");
            }
            _db.CompleteVoteClaim(claimKey);
            completed = true;
            foreach (var item in provider.Rewards.Items) player.GiveItem(item.ItemId, item.Stack, item.Prefix);
            foreach (var group in provider.Rewards.Groups)
            {
                if (!TShock.Groups.GroupExists(group.Group))
                    throw new InvalidOperationException($"Configured vote reward group does not exist: {group.Group}");
                TShockAPI.Commands.HandleCommand(TSPlayer.Server,
                    $"/tempgroup \"{player.Name.Replace("\"", "")}\" \"{group.Group.Replace("\"", "")}\" {group.DurationMinutes}m");
            }
        }
        catch
        {
            if (!completed) _db.ReleaseVoteClaim(claimKey);
            throw;
        }

        var rewardText = atomic > 0 ? cfg.Format(atomic) : "vote rewards";
        player.SendSuccessMessage($"You earned {rewardText} for voting on {provider.DisplayName}.");
        if (cfg.Voting.BroadcastSuccessfulVotes)
            TSPlayer.All.SendSuccessMessage(cfg.Voting.BroadcastMessage.Replace("{PLAYER}", player.Account.Name).Replace("{REWARD}", rewardText));
    }

    private void EnsureCaps(TSPlayer player, VoteProviderConfig provider)
    {
        var cfg = _config().Voting;
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (_db.CountVoteClaims(player.Account.ID, day) >= cfg.MaximumRewardedVotesPerAccountPerDay)
            throw new InvalidOperationException("You have reached the server's daily vote-reward cap.");
        if (_db.CountVoteClaims(player.Account.ID, day, provider.Id) >= provider.MaximumClaimsPerAccountPerDay)
            throw new InvalidOperationException($"You have already received today's {provider.DisplayName} reward.");
    }

    private async Task<string> GetText(string url, int timeoutSeconds, string? userAgent = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }
        using var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        if (body.Length > 4096) throw new InvalidOperationException("Vote provider response was too large.");
        return body;
    }

    public void Dispose() => _http.Dispose();

    private sealed class TServerWebResponse
    {
        [JsonProperty("response")] public string Response { get; set; } = "";
        [JsonProperty("message")] public string Message { get; set; } = "";
    }
}
