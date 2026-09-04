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
        yield return new Command(Permissions.Vote, Vote, "vote")
        {
            AllowServer = false,
            HelpText = "/vote links|claim [provider]|status or /vote tserverweb [captcha-answer]"
        };
    }

    private async void Vote(CommandArgs args)
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
            if (action == "tserverweb")
            {
                var provider = cfg.Providers.FirstOrDefault(p => p.Enabled && p.Type == "TServerWeb")
                    ?? throw new InvalidOperationException("TServerWeb voting is not configured.");
                EnsureCaps(args.Player, provider);
                await ProcessTServerWeb(args.Player, provider, args.Parameters.Skip(1).FirstOrDefault());
                return;
            }
            if (action != "claim") throw new InvalidOperationException("Use /vote links, /vote claim [provider], /vote status, or /vote tserverweb.");
            EnforceCooldown(args.Player.Account.ID, cfg);
            var requested = args.Parameters.Skip(1).FirstOrDefault()?.ToLowerInvariant();
            var providers = cfg.Providers.Where(p => p.Enabled && p.Type == "TerrariaServers" &&
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
            args.Player.SendErrorMessage("[Vote Rewards] " + ex.Message);
            EconomyLog.Warn($"[VoteRewards] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowLinks(TSPlayer player, VotingConfig cfg)
    {
        player.SendInfoMessage("Vote for this server and earn configured rewards:");
        foreach (var provider in cfg.Providers.Where(p => p.Enabled))
            player.SendInfoMessage($"{provider.DisplayName}: {(provider.VotingUrl.Length > 0 ? provider.VotingUrl : "ask an administrator for the voting link")}");
        player.SendInfoMessage("After voting on Terraria-Servers.com use /vote claim. For TServerWeb use /vote tserverweb.");
    }

    private void ShowStatus(TSPlayer player, VotingConfig cfg)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var total = _db.CountVoteClaims(player.Account.ID, day);
        player.SendInfoMessage($"Vote rewards claimed today: {total}/{cfg.MaximumRewardedVotesPerAccountPerDay} (UTC).");
        foreach (var provider in cfg.Providers.Where(p => p.Enabled))
            player.SendInfoMessage($"{provider.DisplayName}: {_db.CountVoteClaims(player.Account.ID, day, provider.Id)}/{provider.MaximumClaimsPerAccountPerDay}");
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
            throw new InvalidOperationException("Start the TServerWeb vote with /vote tserverweb before answering a CAPTCHA.");
        var url = "https://www.tserverweb.com/vote.php?user=" + Uri.EscapeDataString(player.Account.Name) +
                  "&sid=" + Uri.EscapeDataString(provider.ServerId);
        if (!string.IsNullOrWhiteSpace(answer)) url += "&answer=" + Uri.EscapeDataString(answer);
        var json = await GetText(url, provider.TimeoutSeconds);
        var response = JsonConvert.DeserializeObject<TServerWebResponse>(json)
            ?? throw new InvalidOperationException("TServerWeb returned an invalid response.");
        switch (response.Response)
        {
            case "captcha":
                _tserverWebCaptcha[player.Account.ID] = response.Message;
                player.SendInfoMessage("[TServerWeb] CAPTCHA: " + response.Message);
                player.SendInfoMessage("Answer with /vote tserverweb <answer>");
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
        var claimKey = $"vote:{provider.Id}:{player.Account.ID}:{day}";
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
                Commands.HandleCommand(TSPlayer.Server,
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

    private async Task<string> GetText(string url, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = await _http.GetAsync(url, cts.Token);
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
