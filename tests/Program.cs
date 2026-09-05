using System.Net;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Integrations;
using Microsoft.Data.Sqlite;

var checks = 0;
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}
void Reject(Action action)
{
    try { action(); } catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Expected rejection");
}
async Task RejectAsync(Func<Task> action)
{
    try { await action(); } catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Expected rejection");
}
var databasePath = Path.Combine(Path.GetTempPath(), $"arkovia-test-{Guid.NewGuid():N}.sqlite");
var configDirectory = Path.Combine(Path.GetTempPath(), $"arkovia-config-test-{Guid.NewGuid():N}");
var manager = new ConfigManager(configDirectory);
manager.Load();
var active = manager.Current;
File.WriteAllText(manager.FilePath, "{\"GameplayEconomy\":{\"Death\":{\"PenaltyPercent\":101}}}");
Reject(manager.Load);
Equal(active, manager.Current);
File.WriteAllText(manager.FilePath, "{\"CurrencyId\":\"123\"}");
Reject(manager.Load);
Equal(active, manager.Current);
File.Delete(manager.FilePath);
Reject(manager.Load);
Equal(active, manager.Current);
Directory.Delete(configDirectory);
using var connection = new SqliteConnection($"Data Source={databasePath}");
connection.Open();
var db = new EconomyDatabase(connection);
db.EnsureSchema();
Equal(0, db.CountVoteClaims(42, "2026-09-04"));
Equal(true, db.TryReserveVoteClaim("vote:test:42:2026-09-04", 42, "Voter", "test", "2026-09-04", 10, "[]", "[]"));
Equal(false, db.TryReserveVoteClaim("vote:test:42:2026-09-04", 42, "Voter", "test", "2026-09-04", 10, "[]", "[]"));
Equal(1, db.CountVoteClaims(42, "2026-09-04"));
db.CompleteVoteClaim("vote:test:42:2026-09-04");
Equal(1, db.CountVoteClaims(42, "2026-09-04", "test"));
var cfg = new EconomyConfig();
var economy = new EconomyService(db, () => cfg);
var player = economy.GetOrCreatePlayer(1, "Player");
var winner = economy.GetOrCreatePlayer(2, "Winner");
var treasury = economy.GetTreasury();
db.SetBalances(player.Id, cfg.ToAtomic(100), cfg.ToAtomic(50));
Equal(25m, cfg.GameplayEconomy.Death.PenaltyPercent);
Equal(cfg.ToAtomic(25), economy.ApplyPercentageDeathLoss(player, 25, 0, "death1", "Player"));
Equal(cfg.ToAtomic(75), db.GetAccountById(player.Id)!.WalletAtomic);
Equal(cfg.ToAtomic(50), db.GetAccountById(player.Id)!.BankAtomic);
Equal(cfg.ToAtomic(25), economy.GetTreasury().WalletAtomic);
Equal(0L, economy.ApplyPercentageDeathLoss(player, 0, 0, "disabled", "Player"));
Equal(cfg.ToAtomic(5), economy.ApplyPercentageDeathLoss(player, 25, cfg.ToAtomic(70), "protected", "Player"));
Equal(cfg.ToAtomic(70), economy.ApplyPercentageDeathLoss(player, 100, 0, "all", "Player"));
Equal(0L, economy.ApplyPercentageDeathLoss(player, 25, 0, "empty", "Player"));
db.SetBalances(player.Id, 3, cfg.ToAtomic(50));
Equal(0L, economy.ApplyPercentageDeathLoss(player, 25, 0, "dust", "Player"));
Reject(() => economy.ApplyPercentageDeathLoss(player, 101, 0, "invalid", "Player"));
db.SetBalances(player.Id, 100, cfg.ToAtomic(50));
var before = economy.GetTreasury().WalletAtomic;
var pvp = economy.ApplyGameplayPvpLoss(player, winner, 100, 0, 75, "pvp", "PvP", "Player");
Equal(75L, pvp.WinnerAmountAtomic);
Equal(25L, pvp.TreasuryAmountAtomic);
Equal(before + 25, economy.GetTreasury().WalletAtomic);
Equal(100L, pvp.WinnerAmountAtomic + pvp.TreasuryAmountAtomic);
Equal(cfg.ToAtomic(50), db.GetAccountById(player.Id)!.BankAtomic);
economy.AdminAdjust(treasury, 50, "Admin treasury addition", "Server");
Equal(before + 75, economy.GetTreasury().WalletAtomic);
economy.AdminAdjust(treasury, -50, "Admin treasury deduction", "Server");
Equal(before + 25, economy.GetTreasury().WalletAtomic);
Reject(() => economy.AdminAdjust(treasury, -long.MaxValue, "overdraw", "Server"));
Equal("Server", db.GetTransactions(treasury.Id, 1)[0].Actor);
var treasuryBeforeFailure = economy.GetTreasury().WalletAtomic;
db.SetBalances(player.Id, 100, 500);
using (var command = connection.CreateCommand())
{
    command.CommandText = "CREATE TRIGGER fail_ledger BEFORE INSERT ON ArkoviaEconomyTransactions BEGIN SELECT RAISE(ABORT, 'simulated ledger failure'); END";
    command.ExecuteNonQuery();
}
try { economy.ApplyPercentageDeathLoss(player, 25, 0, "rollback", "Player"); throw new Exception("Expected failure"); }
catch (SqliteException) { checks++; }
Equal(100L, db.GetAccountById(player.Id)!.WalletAtomic);
Equal(treasuryBeforeFailure, economy.GetTreasury().WalletAtomic);
try { economy.AdminAdjust(treasury, 100, "rollback", "Server"); throw new Exception("Expected failure"); }
catch (SqliteException) { checks++; }
Equal(treasuryBeforeFailure, economy.GetTreasury().WalletAtomic);
using (var command = connection.CreateCommand())
{
    command.CommandText = "DROP TRIGGER fail_ledger";
    command.ExecuteNonQuery();
}
db.SetBalances(player.Id, 100, cfg.ToAtomic(50));
db.BindCurrency(cfg);
cfg.Decimals = 6;
Reject(() => db.BindCurrency(cfg));
cfg.Decimals = 8;
cfg.CurrencyId = "123";
Reject(() => db.BindCurrency(cfg));
cfg.AcceptExistingBalancesForCurrencyChange = true;
db.BindCurrency(cfg);
Equal(cfg.ToAtomic(50), db.GetAccountById(player.Id)!.BankAtomic);
Equal("123:8", db.GetState("economy.denomination"));
Equal("native:8", db.GetState("economy.previous_denomination"));

var handler = new NodeHandler();
using var node = new ArkoviaNodeClient(() => cfg, handler);
handler.Response = "{\"currency\":\"123\",\"name\":\"Velorium\",\"code\":\"VELR\",\"decimals\":2}";
await node.ValidateCurrencyAsync(default);
Equal("VELR", cfg.CurrencySymbol);
Equal("Velorium", cfg.CurrencyName);
Equal(2, cfg.BlockchainDecimals);
Equal(cfg.ToAtomic(1.23m), cfg.BlockchainToAtomic(123));
Equal("1.23 VELR", cfg.FormatBlockchain(123));
handler.Response = "{\"currency\":\"123\",\"units\":\"123\",\"unconfirmedUnits\":\"999\"}";
Equal(123L, await node.GetAccountBalanceAtomicAsync("ARK-test", default));
Equal(true, handler.Url!.Contains("getAccountCurrencies"));
Equal(true, handler.Url!.Contains("currency=123"));
handler.Response = "{}";
Equal(0L, await node.GetAccountBalanceAtomicAsync("ARK-test", default));
handler.Response = "{\"currency\":\"999\",\"units\":\"100\"}";
await RejectAsync(() => node.GetAccountBalanceAtomicAsync("ARK-test", default));
handler.Response = "{\"errorCode\":5,\"errorDescription\":\"Unknown currency\"}";
await RejectAsync(() => node.ValidateCurrencyAsync(default));
handler.Response = "{\"currency\":\"123\",\"name\":\"Invalid\",\"code\":\"BAD\",\"decimals\":9}";
await RejectAsync(() => node.ValidateCurrencyAsync(default));
handler.Response = "{\"entries\":[{\"holdingType\":\"NXT_BALANCE\",\"change\":\"100\"},{\"holdingType\":\"CURRENCY_BALANCE\",\"holding\":\"999\",\"change\":\"100\"},{\"holdingType\":\"CURRENCY_BALANCE\",\"holding\":\"123\",\"change\":\"123\",\"balance\":\"500\",\"event\":\"1\",\"block\":\"2\"}]}";
var ledger = await node.GetTreasuryLedgerAsync(default);
Equal(1, ledger.Count);
Equal(123L, ledger[0].ChangeAtomic);
Equal(true, handler.Url!.Contains("holding=123"));
Equal(true, ledger[0].ExternalKey.StartsWith("currency:123:"));
cfg.CurrencyId = "";
await node.ValidateCurrencyAsync(default);
Equal(8, cfg.BlockchainDecimals);
handler.Response = "{\"balanceNQT\":\"100000000\"}";
Equal(100000000L, await node.GetAccountBalanceAtomicAsync(default));
Equal(true, handler.Url!.Contains("requestType=getAccount&"));
handler.Response = "{\"balanceNQT\":\"invalid\"}";
await RejectAsync(() => node.GetAccountBalanceAtomicAsync(default));
cfg.CurrencyId = "123";
cfg.BlockchainDecimals = 2;
Equal("CURRENCY_TRANSFER", cfg.FundingEventType);
handler.ResponseForUrl = url => url.Contains("getBlockchainStatus")
    ? "{\"numberOfBlocks\":1601}"
    : "{\"entries\":[{\"holdingType\":\"CURRENCY_BALANCE\",\"holding\":\"123\",\"change\":\"123\",\"balance\":\"500\",\"event\":\"10\",\"block\":\"20\",\"height\":1501,\"eventType\":\"CURRENCY_TRANSFER\"}]}";
using var sync = new ArkoviaFundingSynchronizer(node, db, economy, () => cfg);
var preFunding = economy.GetTreasury().WalletAtomic;
Equal(1, await sync.SyncOnceAsync());
Equal(preFunding + cfg.ToAtomic(1.23m), economy.GetTreasury().WalletAtomic);
Equal(0, await sync.SyncOnceAsync());
Equal(preFunding + cfg.ToAtomic(1.23m), economy.GetTreasury().WalletAtomic);
checks += await SettlementTests.RunAsync();
checks += ProgressionTests.Run();
checks += PortalCodeTests.Run();
checks += AtomicSettlementTests.Run();
checks += AssetOwnershipTests.Run();
checks += TownTests.Run();
checks += TownGovernanceTests.Run();
connection.Close();
SqliteConnection.ClearAllPools();
File.Delete(databasePath);
Console.WriteLine($"PASS: {checks} regression checks (real SQLite economy + simulated node responses).");

sealed class NodeHandler : HttpMessageHandler
{
    public string Response { get; set; } = "{}";
    public string? Url { get; private set; }
    public Func<string, string>? ResponseForUrl { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Url = request.RequestUri!.ToString();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseForUrl?.Invoke(Url) ?? Response) });
    }
}
