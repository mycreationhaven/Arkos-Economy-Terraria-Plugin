using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using ArkoviaEconomy.Api;
using ArkoviaEconomy.Commands;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Integrations;
using ArkoviaEconomy.Gameplay;

namespace ArkoviaEconomy;

[ApiVersion(2, 1)]
public sealed class ArkoviaEconomyPlugin : TerrariaPlugin
{
    private ConfigManager? _config;
    private EconomyDatabase? _database;
    private EconomyService? _economy;
    private ArkoviaNodeClient? _node;
    private WalletClaimClient? _walletClaimClient;
    private ArkoviaFundingSynchronizer? _sync;
    private GameplayEconomyHandler? _gameplay;
    private List<Command> _commands = new();

    public override string Name => "Arkovia Economy";

    public override string Author =>
        "My Creation Haven";

    public override string Description =>
        "Treasury-backed Arkovia economy framework for TShock/Terraria.";

    public override Version Version =>
        new(1, 0, 0);

    public ArkoviaEconomyPlugin(Main game)
        : base(game)
    {
    }

    public override void Initialize()
    {
        _config = new ConfigManager();
        _config.Load();

        _database =
            new EconomyDatabase(TShock.DB);

        _database.EnsureSchema();

        _economy =
            new EconomyService(
                _database,
                () => _config.Current);

        _economy.GetTreasury();

        _node =
            new ArkoviaNodeClient(
                () => _config.Current);

        _walletClaimClient = new WalletClaimClient();

        _sync =
            new ArkoviaFundingSynchronizer(
                _node,
                _database,
                _economy,
                () => _config.Current);

        var handlers =
            new EconomyCommands(
                _economy,
                _database,
                _config,
                _sync,
                _node,
                _walletClaimClient);

        _commands =
            handlers.Build().ToList();

        TShockAPI.Commands.ChatCommands.AddRange(
            _commands);

        ArkoviaEconomyApi.Instance =
            new ArkoviaEconomyApi(_economy);

        _gameplay =
            new GameplayEconomyHandler(
                this,
                _economy,
                () => _config.Current);

        _gameplay.Register();

        _sync.Start();

        TShock.Log.ConsoleInfo(
            $"[ArkoviaEconomy] v{Version} initialized. " +
            $"Treasury source: " +
            $"{_config.Current.Arkovia.CommunityDevelopmentAccount}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var command in _commands)
            {
                TShockAPI.Commands.ChatCommands.Remove(
                    command);
            }

            _commands.Clear();

            _gameplay?.Dispose();
            _sync?.Dispose();
            _node?.Dispose();

            ArkoviaEconomyApi.Instance = null;
        }

        base.Dispose(disposing);
    }
}
