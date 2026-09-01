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
        var cfg = _config();
        if (!cfg.Arkovia.Enabled) { LastStatus = "Arkovia integration disabled"; return 0; }
        var height = await _client.GetHeightAsync(ct);
        var entries = await _client.GetTreasuryLedgerAsync(ct);
        var credited = 0;
        foreach (var e in entries.OrderBy(x => x.Height))
        {
            if (e.Height < cfg.Arkovia.FeeDistributionActivationHeight) continue;
            if (!e.EventType.Equals(cfg.Arkovia.ExpectedLedgerEventType, StringComparison.OrdinalIgnoreCase)) continue;
            if (cfg.Arkovia.CreditOnlyPositiveLedgerChanges && e.ChangeAtomic <= 0) continue;
            var confirmations = height - e.Height + 1;
            if (confirmations < cfg.Arkovia.MinimumConfirmations) continue;
            if (_db.FundingExists(e.ExternalKey)) continue;
            var gameAtomic = checked((long)Math.Floor(e.ChangeAtomic * cfg.Arkovia.GameAllocationPercent / 100m));
            _db.InsertFunding(e, gameAtomic);
            if (gameAtomic > 0)
            {
                _economy.CreditTreasury(gameAtomic, "funding:" + e.ExternalKey, e.EventId, $"Confirmed Arkovia 5% fee distribution at height {e.Height} ({confirmations} confirmations)");
                credited++;
            }
        }
        LastSuccessUtc = DateTime.UtcNow;
        LastStatus = $"Synced height {height}; credited {credited} new funding entr{(credited == 1 ? "y" : "ies")}.";
        _db.SetState("arkovia.last_height", height.ToString());
        _db.SetState("arkovia.last_sync_utc", LastSuccessUtc.Value.ToString("O"));
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
