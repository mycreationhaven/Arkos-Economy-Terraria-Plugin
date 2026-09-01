using Newtonsoft.Json;

namespace ArkoviaEconomy.Config;

public sealed class EconomyConfig
{
    public string CurrencyName { get; set; } = "Arkos";
    public string CurrencySymbol { get; set; } = "ARK";
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

public sealed class ApiConfig
{
    public bool EnablePublicReadApi { get; set; } = true;
    public bool ExposePlayerBalances { get; set; } = false;
}
