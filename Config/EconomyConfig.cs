using Newtonsoft.Json;

namespace ArkoviaEconomy.Config;

public sealed class EconomyConfig
{
    public ArkoviaEconomy.Progression.ProgressionConfig Progression { get; set; } = new();
    // Off-chain Decimals is independent of node currency decimals and never auto-rescaled.
    public BlockchainTransferConfig Transfers { get; set; } = new();
    public SecurityPortalConfig SecurityPortal { get; set; } = new();
    public EventRewardsConfig EventRewards { get; set; } = new();
    public VotingConfig Voting { get; set; } = new();

    public string CurrencyId { get; set; } = "";
    public bool AcceptExistingBalancesForCurrencyChange { get; set; } = false;
    [JsonIgnore]
    public string FundingEventType => CurrencyId.Length > 0 && Arkovia.ExpectedLedgerEventType == "BLOCK_GENERATED"
        ? "CURRENCY_TRANSFER" : Arkovia.ExpectedLedgerEventType;
    [JsonIgnore]
    public int BlockchainDecimals { get; set; } = 8;
    public long BlockchainToAtomic(long units) => checked((long)Math.Floor(
        units * (decimal)AtomicUnit / Pow10(BlockchainDecimals)));
    public long AtomicToBlockchainExact(long atomic)
    {
        var units = atomic * (decimal)Pow10(BlockchainDecimals) / AtomicUnit;
        if (units <= 0 || units != decimal.Truncate(units))
            throw new InvalidOperationException("Amount cannot be represented at the blockchain currency precision.");
        return checked((long)units);
    }
    public long BlockchainToAtomicExact(long units)
    {
        var atomic = units * (decimal)AtomicUnit / Pow10(BlockchainDecimals);
        if (atomic <= 0 || atomic != decimal.Truncate(atomic))
            throw new InvalidOperationException("Deposit cannot be represented at the economy precision.");
        return checked((long)atomic);
    }
    public string FormatBlockchain(long units) =>
        $"{units / (decimal)Pow10(BlockchainDecimals):0.########} {CurrencySymbol}";

    public string CurrencyName { get; set; } = "ARKOS";
    public string CurrencySymbol { get; set; } = "ARKOS";
    public int Decimals { get; set; } = 8;
    public decimal StartingBalance { get; set; } = 0m;
    public decimal MinimumTransfer { get; set; } = 0.01m;
    public decimal MaximumPlayerBalance { get; set; } = 1_000_000_000m;
    public decimal PlayerTransferFeePercent { get; set; } = 0m;
    public decimal ShopSalesTaxPercent { get; set; } = 0m;
    public decimal MarketListingFeePercent { get; set; } = 1m;
    public decimal MarketSalesTaxPercent { get; set; } = 2m;
    public bool ReturnServerFeesToTreasury { get; set; } = true;
    public BankingConfig Banking { get; set; } = new();
    public ArkoviaConfig Arkovia { get; set; } = new();
    public RewardConfig Rewards { get; set; } = new();
    public GameplayEconomyConfig GameplayEconomy { get; set; } = new();
    public ApiConfig Api { get; set; } = new();

    [JsonIgnore]
    public long AtomicUnit => Pow10(Decimals);

    public long ToAtomic(decimal amount) => checked((long)Math.Round(amount * AtomicUnit, 0, MidpointRounding.AwayFromZero));
    public decimal FromAtomic(long atomic) => atomic / (decimal)AtomicUnit;
    public string Format(long atomic) => $"{FromAtomic(atomic):0.########} {CurrencySymbol}";

    private static long Pow10(int n)
    {
        long value = 1;
        for (var i = 0; i < n; i++) value = checked(value * 10);
        return value;
    }
}

public sealed class VotingConfig
{
    public bool Enabled { get; set; } = false;
    public int MaximumRewardedVotesPerAccountPerDay { get; set; } = 2;
    public int ClaimCooldownSeconds { get; set; } = 10;
    public bool BroadcastSuccessfulVotes { get; set; } = true;
    public string BroadcastMessage { get; set; } = "{PLAYER} voted for the server and earned {REWARD}!";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<VoteProviderConfig> Providers { get; set; } =
    [
        new() { Id = "terraria-servers", Type = "TerrariaServers", DisplayName = "Terraria-Servers.com" },
        new() { Id = "tserverweb", Type = "TServerWeb", DisplayName = "TServerWeb.com" }
    ];
}

public sealed class VoteProviderConfig
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = false;
    public string ServerId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string VotingUrl { get; set; } = "";
    public int MaximumClaimsPerAccountPerDay { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 10;
    public VoteRewardPackage Rewards { get; set; } = new();
}

public sealed class VoteRewardPackage
{
    public decimal CurrencyAmount { get; set; } = 0m;
    public List<VoteItemReward> Items { get; set; } = [];
    public List<VoteGroupReward> Groups { get; set; } = [];
}

public sealed class VoteItemReward
{
    public int ItemId { get; set; }
    public int Stack { get; set; } = 1;
    public int Prefix { get; set; }
}

public sealed class VoteGroupReward
{
    public string Group { get; set; } = "";
    public int DurationMinutes { get; set; } = 1440;
}

public sealed class BankingConfig
{
    public bool Enabled { get; set; } = true;
    public decimal DepositFeePercent { get; set; } = 0m;
    public decimal WithdrawalFeePercent { get; set; } = 0m;
    public decimal InterestAprPercent { get; set; } = 0m;
    public int InterestIntervalHours { get; set; } = 24;
    public decimal MaximumInterestPerInterval { get; set; } = 100m;
}

public sealed class ArkoviaConfig
{
    public bool Enabled { get; set; } = true;
    public string NodeUrl { get; set; } = "http://127.0.0.1:4876/nxt";
    public string CommunityDevelopmentAccount { get; set; } = "ARK-KVFL-C6EE-2UD2-CSJ8Q";
    public string ExpectedLedgerEventType { get; set; } = "BLOCK_GENERATED";
    public int MinimumConfirmations { get; set; } = 10;
    public int PollSeconds { get; set; } = 60;
    public int LedgerPageSize { get; set; } = 100;
    public decimal GameAllocationPercent { get; set; } = 100m;
    public int FeeDistributionActivationHeight { get; set; } = 1500;
    public bool CreditOnlyPositiveLedgerChanges { get; set; } = true;
    public bool RequireNodeToBeLocalOrHttps { get; set; } = true;
}

public sealed class RewardConfig
{
    public bool Enabled { get; set; } = true;
    public bool EnforceTreasurySolvency { get; set; } = true;
    public decimal DailyReward { get; set; } = 0m;
    public decimal NewPlayerReward { get; set; } = 0m;
}

public sealed class GameplayEconomyConfig
{
    public bool Enabled { get; set; } = true;

    // Supported:
    // PlayerOnly, Nearby, Global, Silent
    public string DefaultBroadcastMode { get; set; } = "Silent";

    // Diagnostic logging for NPC reward attribution and payout decisions.
    public bool LogNpcRewardDecisions { get; set; } = true;

    // Target display duration for custom floating reward UI.
    // Terraria packet 119 currently controls its own client-side lifetime,
    // so this value is reserved until a safe custom renderer is added.
    public double FloatingRewardTextDurationSeconds { get; set; } = 3.5;

    public GameplayRewardRangesConfig Rewards { get; set; } = new();
    public GameplayDeathConfig Death { get; set; } = new();
    public GameplayPvpConfig PvP { get; set; } = new();
}

public sealed class GameplayRewardRangesConfig
{
    public GameplayRewardRange CommonEnemy { get; set; } =
        new(0.0001m, 0.001m);

    public GameplayRewardRange StrongRareEnemy { get; set; } =
        new(0.005m, 0.05m);

    public GameplayRewardRange EarlyBoss { get; set; } =
        new(0.10m, 0.25m);

    public GameplayRewardRange MidBoss { get; set; } =
        new(0.25m, 0.50m);

    public GameplayRewardRange EndGameBoss { get; set; } =
        new(0.50m, 1.00m);

    public GameplayRewardRange Quest { get; set; } =
        new(0.05m, 0.25m);
}

public sealed class GameplayRewardRange
{
    public GameplayRewardRange()
    {
    }

    public GameplayRewardRange(
        decimal minimum,
        decimal maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public bool Enabled { get; set; } = true;
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; }
}

public sealed class GameplayDeathConfig
{
    public bool Enabled { get; set; } = true;

    // Percentage of the wallet; capped to preserve MinimumProtectedBalance.
    public decimal PenaltyPercent { get; set; } = 25m;

    // Gameplay penalties affect the wallet balance only.
    // Banked currency remains protected.
    public decimal MinimumProtectedBalance { get; set; } = 0m;

    public int CooldownSeconds { get; set; } = 60;

    public bool ShowZeroBalanceMessage { get; set; } = true;
}

public sealed class GameplayPvpConfig
{
    public bool Enabled { get; set; } = true;

    public decimal Penalty { get; set; } = 0.01m;

    public decimal MinimumProtectedBalance { get; set; } = 0m;

    public decimal WinnerPercent { get; set; } = 75m;

    public decimal TreasuryPercent { get; set; } = 25m;

    public int CooldownSeconds { get; set; } = 60;
}

public sealed class ApiConfig
{
    public bool EnablePublicReadApi { get; set; } = true;
    public bool ExposePlayerBalances { get; set; } = false;
}
