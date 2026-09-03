using Terraria;
using ArkoviaEconomy.Security;
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
    private ArkoviaEconomy.Progression.ProgressionHandler? _progression;
    private ConfigManager? _config;
    private EconomyDatabase? _database;
    private EconomyService? _economy;
    private ArkoviaNodeClient? _node;
    private WalletClaimClient? _walletClaimClient;
    private ArkoviaFundingSynchronizer? _sync;
    private GameplayEconomyHandler? _gameplay;
    private BlockchainTransferService? _transfers;
    private SecurityPortal? _portal;
    private List<Command> _commands = new();

    public override string Name => "Arkovia Economy";

    public override string Author =>
        "My Creation Haven";

    public override string Description =>
        "Treasury-backed Arkovia economy framework for TShock/Terraria.";

    public override Version Version =>
        new(1, 3, 1);

    public ArkoviaEconomyPlugin(Main game)
        : base(game)
    {
    }

    public override void Initialize()
    {
        _config = new ConfigManager();
        EconomyLog.Initialize(_config.DirectoryPath);
        _config.Load();

        _database =
            new EconomyDatabase(TShock.DB);

        _database.EnsureSchema();

        _node = new ArkoviaNodeClient(() => _config.Current);
        // Validate before registering commands, rewards or funding synchronization.
        _node.ValidateCurrencyAsync(CancellationToken.None).GetAwaiter().GetResult();
        _database.BindCurrency(_config.Current);

        _economy =
            new EconomyService(
                _database,
                () => _config.Current);

        _economy.GetTreasury();

        _walletClaimClient = new WalletClaimClient();

        _sync =
            new ArkoviaFundingSynchronizer(
                _node,
                _database,
                _economy,
                () => _config.Current);

        _transfers = new BlockchainTransferService(_database, _economy, _node, () => _config.Current);
        _transfers.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _portal = new SecurityPortal(_database, new TransactionPinService(_database), _transfers,
            () => _config.Current, (userId, permission) => TShock.Players.Any(p =>
                p is { Active: true, IsLoggedIn: true } && p.Account?.ID == userId && p.HasPermission(permission)));
        _portal.Start();

        var handlers =
            new EconomyCommands(
                _economy,
                _database,
                _config,
                _sync,
                _node,
                _walletClaimClient, _transfers, _portal);

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

        _progression = new ArkoviaEconomy.Progression.ProgressionHandler(this,
            new ArkoviaEconomy.Progression.ProgressionService(_database, _economy, () => _config.Current),
            _economy, () => _config.Current);
        _progression.Register();

        _sync.Start();
        _transfers.Start();

        ArkoviaEconomy.Core.EconomyLog.Info(
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

            _progression?.Dispose();
            _gameplay?.Dispose();
            _portal?.Dispose();
            _transfers?.Dispose();
            _sync?.Dispose();
            _node?.Dispose();

            ArkoviaEconomyApi.Instance = null;
        }

        base.Dispose(disposing);
    }
}
