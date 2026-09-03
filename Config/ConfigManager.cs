using Newtonsoft.Json;
using TShockAPI;

namespace ArkoviaEconomy.Config;

public sealed class ConfigManager
{
    public string DirectoryPath { get; }
    public string FilePath { get; }
    public EconomyConfig Current { get; private set; } = new();

    public ConfigManager(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(TShock.SavePath, "ArkoviaEconomy");
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    private bool _loaded;

    public void Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath))
        {
            if (_loaded)
                throw new InvalidOperationException("Configuration file is missing. Active configuration was retained.");
            Current = new EconomyConfig();
            Save();
            _loaded = true;
            return;
        }

        var json = File.ReadAllText(FilePath);
        var candidate = JsonConvert.DeserializeObject<EconomyConfig>(json)
            ?? throw new InvalidOperationException("Configuration cannot be null.");
        Validate(candidate);
        if (_loaded)
        {
            if (candidate.CurrencyId != Current.CurrencyId || candidate.Decimals != Current.Decimals ||
                candidate.Arkovia.NodeUrl != Current.Arkovia.NodeUrl ||
                JsonConvert.SerializeObject(candidate.Transfers) != JsonConvert.SerializeObject(Current.Transfers) ||
                JsonConvert.SerializeObject(candidate.SecurityPortal) != JsonConvert.SerializeObject(Current.SecurityPortal) ||
                candidate.Arkovia.CommunityDevelopmentAccount != Current.Arkovia.CommunityDevelopmentAccount)
                throw new InvalidOperationException("Currency, decimals, node and source-account changes require a server restart. Active configuration was retained.");
            candidate.BlockchainDecimals = Current.BlockchainDecimals;
            if (candidate.CurrencyId.Length > 0)
            {
                candidate.CurrencyName = Current.CurrencyName;
                candidate.CurrencySymbol = Current.CurrencySymbol;
            }
        }
        Current = candidate;
        _loaded = true;
    }

    public void Save() => File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));

    private static void Validate(EconomyConfig cfg)
    {
        cfg.CurrencyId = (cfg.CurrencyId ?? "").Trim();
        if (cfg.CurrencyId.Length > 0)
        {
            if (!ulong.TryParse(cfg.CurrencyId, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id == 0)
                throw new InvalidOperationException("CurrencyId must be a positive numeric Arkovia currency ID, or blank for ARKOS.");
            cfg.CurrencyId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (cfg.Decimals is < 0 or > 8) throw new InvalidOperationException("Decimals must be between 0 and 8.");
        if (cfg.Arkovia.GameAllocationPercent is < 0 or > 100) throw new InvalidOperationException("GameAllocationPercent must be 0-100.");
        if (cfg.Arkovia.PollSeconds < 15) cfg.Arkovia.PollSeconds = 15;
        if (cfg.Arkovia.LedgerPageSize is < 1 or > 1000) cfg.Arkovia.LedgerPageSize = 100;


        if (cfg.EventRewards.MinimumDamage < 1 || cfg.EventRewards.MinimumDurationSeconds < 0 ||
            cfg.EventRewards.Pools.Any(p => p.Value < 0))
            throw new InvalidOperationException("Invalid event reward limits.");
        var transfer = cfg.Transfers;
        if (transfer.Confirmations < 1 || transfer.PollSeconds < 15 || transfer.MinimumWithdrawal <= 0 ||
            transfer.MaximumWithdrawal < transfer.MinimumWithdrawal || transfer.DailyWithdrawalLimit < transfer.MaximumWithdrawal ||
            transfer.MinimumReserve < 0 || transfer.MaximumNetworkFeeArkos <= 0 ||
            transfer.StarterGrant.Amount <= 0 || transfer.StarterGrant.MaximumPerDay < 1)
            throw new InvalidOperationException("Invalid blockchain transfer limits.");
        if (transfer.Enabled && (string.IsNullOrWhiteSpace(transfer.ReserveAccount) || !cfg.SecurityPortal.Enabled))
            throw new InvalidOperationException("Transfers require a reserve account and the security portal.");
        if (cfg.SecurityPortal.Enabled)
        {
            if (!Uri.TryCreate(cfg.SecurityPortal.ListenUrl, UriKind.Absolute, out var listen) ||
                !listen.IsLoopback || listen.Scheme != "http" || !cfg.SecurityPortal.ListenUrl.EndsWith('/'))
                throw new InvalidOperationException("Security portal must listen on loopback HTTP with a trailing slash.");
            if (!Uri.TryCreate(cfg.SecurityPortal.PublicUrl, UriKind.Absolute, out var publicUrl) ||
                publicUrl.Scheme != "https" || !cfg.SecurityPortal.PublicUrl.EndsWith('/') ||
                cfg.SecurityPortal.SessionMinutes is < 1 or > 10)
                throw new InvalidOperationException("Set an HTTPS PublicUrl with a trailing slash and a 1-10 minute session.");
        }
        if (!Uri.TryCreate(transfer.SignerUrl, UriKind.Absolute, out var signer) || !signer.IsLoopback || signer.Scheme != "http")
            throw new InvalidOperationException("SignerUrl must be loopback HTTP.");
        var gameplay = cfg.GameplayEconomy;

        var allowedBroadcastModes = new[]
        {
            "PlayerOnly",
            "Nearby",
            "Global",
            "Silent"
        };

        if (!allowedBroadcastModes.Any(
                x => x.Equals(
                    gameplay.DefaultBroadcastMode,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "GameplayEconomy.DefaultBroadcastMode must be " +
                "PlayerOnly, Nearby, Global, or Silent.");
        }

        if (gameplay.Death.PenaltyPercent is < 0 or > 100 ||
            gameplay.Death.MinimumProtectedBalance < 0)
        {
            throw new InvalidOperationException(
                "Death PenaltyPercent must be 0-100 and MinimumProtectedBalance cannot be negative.");
        }

        if (gameplay.Death.CooldownSeconds < 0)
            gameplay.Death.CooldownSeconds = 0;

        if (gameplay.PvP.Penalty < 0 ||
            gameplay.PvP.MinimumProtectedBalance < 0)
        {
            throw new InvalidOperationException(
                "Gameplay PvP values cannot be negative.");
        }

        if (gameplay.PvP.WinnerPercent < 0 ||
            gameplay.PvP.TreasuryPercent < 0 ||
            gameplay.PvP.WinnerPercent +
                gameplay.PvP.TreasuryPercent != 100m)
        {
            throw new InvalidOperationException(
                "PvP WinnerPercent + TreasuryPercent must equal 100.");
        }

        if (gameplay.PvP.CooldownSeconds < 0)
            gameplay.PvP.CooldownSeconds = 0;

        ValidateRewardRange(
            gameplay.Rewards.CommonEnemy,
            "CommonEnemy");

        ValidateRewardRange(
            gameplay.Rewards.StrongRareEnemy,
            "StrongRareEnemy");

        ValidateRewardRange(
            gameplay.Rewards.EarlyBoss,
            "EarlyBoss");

        ValidateRewardRange(
            gameplay.Rewards.MidBoss,
            "MidBoss");

        ValidateRewardRange(
            gameplay.Rewards.EndGameBoss,
            "EndGameBoss");

        ValidateRewardRange(
            gameplay.Rewards.Quest,
            "Quest");
    }

    private static void ValidateRewardRange(
        GameplayRewardRange range,
        string name)
    {
        if (range.Minimum < 0 ||
            range.Maximum < 0 ||
            range.Maximum < range.Minimum)
        {
            throw new InvalidOperationException(
                $"Gameplay reward range '{name}' is invalid.");
        }
    }

}
