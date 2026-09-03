namespace ArkoviaEconomy.Config;

public sealed class BlockchainTransferConfig
{
    public bool Enabled { get; set; } = false;
    public string ReserveAccount { get; set; } = "";
    public string SignerUrl { get; set; } = "http://127.0.0.1:4892/prepare";
    public string SignerApiKeyEnvironment { get; set; } = "ARKOVIA_SIGNER_API_KEY";
    public decimal MinimumWithdrawal { get; set; } = 0.01m;
    public decimal MaximumWithdrawal { get; set; } = 100m;
    public decimal DailyWithdrawalLimit { get; set; } = 500m;
    public decimal MinimumReserve { get; set; } = 100m;
    // Network fees are always native ARKOS, paid by the operator's reserve.
    public decimal MaximumNetworkFeeArkos { get; set; } = 1m;
    public int Confirmations { get; set; } = 10;
    public int PollSeconds { get; set; } = 30;
    public StarterGrantConfig StarterGrant { get; set; } = new();
}

public sealed class StarterGrantConfig
{
    public bool Enabled { get; set; } = false;
    public decimal Amount { get; set; } = 10m;
    public int MaximumPerDay { get; set; } = 10;
}

public sealed class SecurityPortalConfig
{
    public bool Enabled { get; set; } = false;
    public string ListenUrl { get; set; } = "http://127.0.0.1:4891/";
    public string PublicUrl { get; set; } = "";
    public int SessionMinutes { get; set; } = 5;
}

public sealed class EventRewardsConfig
{
    public bool Enabled { get; set; } = true;
    public long MinimumDamage { get; set; } = 100;
    public int MinimumDurationSeconds { get; set; } = 30;
    public Dictionary<string, decimal> Pools { get; set; } = new()
    {
        ["DD2Tier1"] = 1m, ["DD2Tier2"] = 2m, ["DD2Tier3"] = 3m,
        ["GoblinArmy"] = 1m, ["FrostLegion"] = 1m,
        ["PirateInvasion"] = 2m, ["MartianMadness"] = 3m,
        ["BloodMoon"] = 1m, ["SolarEclipse"] = 2m,
        ["PumpkinMoon"] = 3m, ["FrostMoon"] = 3m
    };
}
