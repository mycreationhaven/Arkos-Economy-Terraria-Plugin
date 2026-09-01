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
    }
}
