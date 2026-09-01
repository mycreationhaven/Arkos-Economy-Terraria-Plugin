using Newtonsoft.Json;
using TShockAPI;

namespace ArkoviaEconomy.Config;

public sealed class ConfigManager
{
    public string DirectoryPath { get; }
    public string FilePath { get; }
    public EconomyConfig Current { get; private set; } = new();

    public ConfigManager()
    {
        DirectoryPath = Path.Combine(TShock.SavePath, "ArkoviaEconomy");
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    public void Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath))
        {
            Current = new EconomyConfig();
            Save();
            return;
        }

        var json = File.ReadAllText(FilePath);
        Current = JsonConvert.DeserializeObject<EconomyConfig>(json) ?? new EconomyConfig();
        Validate(Current);
    }

    public void Save() => File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));

    private static void Validate(EconomyConfig cfg)
    {
        if (cfg.Decimals is < 0 or > 8) throw new InvalidOperationException("Decimals must be between 0 and 8.");
        if (cfg.Arkovia.GameAllocationPercent is < 0 or > 100) throw new InvalidOperationException("GameAllocationPercent must be 0-100.");
        if (cfg.Arkovia.PollSeconds < 15) cfg.Arkovia.PollSeconds = 15;
        if (cfg.Arkovia.LedgerPageSize is < 1 or > 1000) cfg.Arkovia.LedgerPageSize = 100;


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

        if (gameplay.Death.Penalty < 0 ||
            gameplay.Death.MinimumProtectedBalance < 0)
        {
            throw new InvalidOperationException(
                "Gameplay death values cannot be negative.");
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
