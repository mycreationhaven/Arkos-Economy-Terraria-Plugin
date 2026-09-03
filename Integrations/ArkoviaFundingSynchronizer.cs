using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Integrations;

public sealed class ArkoviaFundingSynchronizer : IDisposable
{
    private readonly ArkoviaNodeClient _client;
    private readonly EconomyDatabase _db;
    private readonly EconomyService _economy;
    private readonly Func<EconomyConfig> _config;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    public DateTime? LastSuccessUtc { get; private set; }
    public string LastStatus { get; private set; } = "Not started";

    public ArkoviaFundingSynchronizer(ArkoviaNodeClient client, EconomyDatabase db, EconomyService economy, Func<EconomyConfig> config)
    { _client = client; _db = db; _economy = economy; _config = config; }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        await _syncGate.WaitAsync(ct);
        try { return await SyncCoreAsync(ct); }
        finally { _syncGate.Release(); }
    }

    private async Task<int> SyncCoreAsync(CancellationToken ct)
    {
        var cfg = _config();
        if (!cfg.Arkovia.Enabled) { LastStatus = "Arkovia integration disabled"; return 0; }
        var height = await _client.GetHeightAsync(ct);
        var entries = await _client.GetTreasuryLedgerAsync(ct);
        var credited = 0;

        foreach (var e in entries.OrderBy(x => x.Height))
        {
            if (e.Height < cfg.Arkovia.FeeDistributionActivationHeight) continue;
            if (!e.EventType.Equals(cfg.FundingEventType, StringComparison.OrdinalIgnoreCase)) continue;
            if (cfg.Arkovia.CreditOnlyPositiveLedgerChanges && e.ChangeAtomic <= 0) continue;

            var confirmations = height - e.Height + 1;
            if (confirmations < cfg.Arkovia.MinimumConfirmations) continue;
            if (_db.FundingExists(e.ExternalKey)) continue;

            var gameAtomic = checked(
                (long)Math.Floor(
                    cfg.BlockchainToAtomic(e.ChangeAtomic) *
                    cfg.Arkovia.GameAllocationPercent /
                    100m));

            _db.InsertFunding(e, gameAtomic);

            if (gameAtomic > 0)
            {
                _economy.CreditTreasury(
                    gameAtomic,
                    "funding:" + e.ExternalKey,
                    e.EventId,
                    $"Confirmed Arkovia currency funding at height {e.Height} ({confirmations} confirmations)");

                credited++;
            }
        }

        // Some Arkovia nodes currently return no account-ledger entries for
        // the Community Development account even though its confirmed balance
        // changes as distributions arrive.
        //
        // In that case, safely fall back to confirmed balance-delta tracking.
        // The first observation establishes a baseline and credits nothing.
        // Only future positive growth is eligible for Terraria funding.
        if (entries.Count == 0)
        {
            var scope = cfg.CurrencyId.Length == 0 ? "native" : cfg.CurrencyId;
            var source = cfg.Arkovia.CommunityDevelopmentAccount;
            var baselineKey = $"arkovia.balance:{scope}:{source}";
            // Carry forward the old native high-water mark once on upgrade.
            if (cfg.CurrencyId.Length == 0 && _db.GetState(baselineKey) is null &&
                _db.GetState("arkovia.legacy_baseline_migrated") is null &&
                _db.GetState("arkovia.treasury_balance_baseline_atomic") is string legacy)
            {
                _db.SetState(baselineKey, legacy);
                _db.SetState("arkovia.legacy_baseline_migrated", source);
            }

            var currentBalance =
                await _client.GetAccountBalanceAtomicAsync(ct);

            var storedBaseline =
                _db.GetState(baselineKey);

            if (!long.TryParse(
                    storedBaseline,
                    out var previousBalance))
            {
                _db.SetState(
                    baselineKey,
                    currentBalance.ToString());

                LastSuccessUtc = DateTime.UtcNow;

                LastStatus =
                    $"Synced height {height}; ledger unavailable; " +
                    $"initialized treasury balance baseline at " +
                    $"{currentBalance} atomic units; credited 0.";

                _db.SetState(
                    "arkovia.last_height",
                    height.ToString());

                _db.SetState(
                    "arkovia.last_sync_utc",
                    LastSuccessUtc.Value.ToString("O"));

                TShock.Log.ConsoleInfo(
                    "[ArkoviaEconomy] Arkovia ledger returned no entries. " +
                    $"Initialized confirmed-balance baseline at {currentBalance} atomic units.");

                return 0;
            }

            if (currentBalance > previousBalance)
            {
                var positiveDelta =
                    checked(currentBalance - previousBalance);

                var gameAtomic =
                    checked(
                        (long)Math.Floor(
                            cfg.BlockchainToAtomic(positiveDelta) *
                            cfg.Arkovia.GameAllocationPercent /
                            100m));

                if (gameAtomic > 0)
                {
                    var externalId =
                        $"funding:balance-delta:{scope}:{source}:{previousBalance}:{currentBalance}";

                    _economy.CreditTreasury(
                        gameAtomic,
                        externalId,
                        height.ToString(),
                        $"Confirmed Arkovia source balance increase from " +
                        $"{previousBalance} to {currentBalance} atomic units");

                    credited++;

                    TShock.Log.ConsoleInfo(
                        "[ArkoviaEconomy] Credited Terraria Treasury from " +
                        $"confirmed Arkovia balance growth. " +
                        $"Previous={previousBalance}, Current={currentBalance}, " +
                        $"Delta={positiveDelta}, Credited={gameAtomic} atomic units.");
                }
            }
            else if (currentBalance < previousBalance)
            {
                TShock.Log.ConsoleWarn(
                    "[ArkoviaEconomy] Arkovia source balance decreased. " +
                    $"HighWater={previousBalance}, Current={currentBalance}. " +
                    "No Terraria debit was applied and the funding high-water mark was retained.");
            }

            // Treat the persisted balance as a high-water mark.
            //
            // Never lower it when the source account decreases. Otherwise,
            // replenishing previously spent ARKOS could be mistaken for new
            // funding and credited to Terraria a second time.
            if (currentBalance > previousBalance)
            {
                _db.SetState(
                    baselineKey,
                    currentBalance.ToString());
            }
        }

        LastSuccessUtc = DateTime.UtcNow;

        LastStatus =
            $"Synced height {height}; credited {credited} new funding " +
            $"entr{(credited == 1 ? "y" : "ies")}.";

        _db.SetState(
            "arkovia.last_height",
            height.ToString());

        _db.SetState(
            "arkovia.last_sync_utc",
            LastSuccessUtc.Value.ToString("O"));

        return credited;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await SyncOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LastStatus = "Sync error: " + ex.Message;
                TShock.Log.ConsoleError("[ArkoviaEconomy] " + LastStatus);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, _config().Arkovia.PollSeconds)), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
    }
}
