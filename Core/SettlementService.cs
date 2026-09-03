using System.Numerics;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Core;

public sealed partial class EconomyService
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int UserId, long Atomic, string Event)> _eventNotices = new();
    public bool TryDequeueEventNotice(out (int UserId, long Atomic, string Event) notice) => _eventNotices.TryDequeue(out notice);
    public T Locked<T>(Func<T> action) { lock (_gate) return action(); }

    public static Dictionary<int, long> AllocatePool(long pool, IReadOnlyDictionary<int, long> damage)
    {
        if (pool <= 0 || damage.Count == 0 || damage.Any(p => p.Key <= 0 || p.Value <= 0))
            throw new InvalidOperationException("Invalid reward pool or contributions.");
        var total = damage.Values.Aggregate(BigInteger.Zero, (sum, value) => sum + value);
        var shares = damage.Select(p => (p.Key, Amount: (long)((BigInteger)pool * p.Value / total),
            Remainder: (BigInteger)pool * p.Value % total)).ToArray();
        var result = shares.ToDictionary(p => p.Key, p => p.Amount);
        var left = pool - result.Values.Sum();
        foreach (var p in shares.OrderByDescending(p => p.Remainder).ThenBy(p => p.Key).Take((int)left)) result[p.Key]++;
        return result;
    }

    public bool QueueEvent(string id, string name, long pool, IReadOnlyDictionary<int, long> damage)
    {
        lock (_gate)
        {
            if (_db.GetOperation(id) is not null) return false;
            var eligible = damage.Where(p => p.Value >= _config().EventRewards.MinimumDamage &&
                _db.GetPlayerAccount(p.Key) is not null).ToDictionary();
            if (eligible.Count == 0 || pool <= 0) return false;
            var shares = AllocatePool(pool, eligible);
            var op = new EconomyOperation(id, "event", 0, "Queued", DateTime.UtcNow,
                _config().CurrencyId, pool, Recipient: name) { Allocations = shares };
            return _db.Atomic(tx => { tx.Insert(op); return true; });
        }
    }
    public bool SettleEvent(string id)
    {
        lock (_gate)
        {
            var op = _db.GetOperation(id) ?? throw new InvalidOperationException("Unknown event.");
            if (op.Status == "Confirmed") return false;
            if (op.Kind != "event" || op.Status != "Queued" || op.CurrencyId != _config().CurrencyId)
                throw new InvalidOperationException("Event is not eligible for settlement.");
            if (op.Allocations is null || op.Allocations.Any(p => p.Value < 0) ||
                op.Allocations.Values.Sum(v => (decimal)v) != op.Atomic)
                throw new InvalidOperationException("Event allocation does not conserve its pool.");
            var treasury = GetTreasury();
            if (treasury.Frozen || treasury.WalletAtomic < op.Atomic) throw new InvalidOperationException("Event awaits treasury funding.");
            var payouts = op.Allocations!.Where(p => p.Value > 0).Select(p =>
                (Account: _db.GetPlayerAccount(p.Key) ?? throw new InvalidOperationException("Missing event participant."), Amount: p.Value)).ToList();
            foreach (var p in payouts)
                if (p.Account.Frozen || checked(p.Account.WalletAtomic + p.Amount) > _config().ToAtomic(_config().MaximumPlayerBalance))
                    throw new InvalidOperationException("Event awaits participant account resolution.");
            _db.Atomic(tx =>
            {
                tx.Wallet(treasury, treasury.WalletAtomic - op.Atomic);
                foreach (var p in payouts)
                {
                    tx.Wallet(p.Account, checked(p.Account.WalletAtomic + p.Amount));
                    tx.Ledger($"{id}:{p.Account.Id}", treasury.Id, p.Account.Id, p.Amount, "event_reward", op.Recipient, "Terraria");
                }
                tx.Update(op with { Status = "Confirmed" }, "Queued");
                return true;
            });
            foreach (var p in payouts) _eventNotices.Enqueue((p.Account.TShockUserId!.Value, p.Amount, op.Recipient));
            return true;
        }
    }
    public bool CreditBlockchainDeposit(int userId, string fullHash, long atomic)
    {
        if (atomic <= 0) throw new InvalidOperationException("Deposit must be positive.");
        lock (_gate)
        {
            var id = "deposit:" + fullHash;
            if (_db.GetOperation(id) is not null) return false;
            var account = _db.GetPlayerAccount(userId) ?? throw new InvalidOperationException("Missing economy account.");
            var next = checked(account.WalletAtomic + atomic);
            if (account.Frozen || next > _config().ToAtomic(_config().MaximumPlayerBalance))
                throw new InvalidOperationException("Deposit exceeds balance limit or account is frozen; retry after resolving it.");
            return _db.Atomic(tx =>
            {
                tx.Insert(new(id, "deposit", userId, "Confirmed", DateTime.UtcNow, _config().CurrencyId, atomic, FullHash: fullHash));
                tx.Wallet(account, next);
                tx.Ledger(id, null, account.Id, atomic, "blockchain_deposit", fullHash, account.Name);
                return true;
            });
        }
    }
    public void HoldWithdrawal(EconomyOperation op)
    {
        lock (_gate)
        {
            if (_db.GetOperation(op.Id) is not null) return;
            if (_db.GetOperation("outgoing:" + op.FullHash) is not null)
                throw new InvalidOperationException("This signed transaction is already reserved or settled. Request a fresh quote.");
            var cfg = _config();
            var prior = _db.Operations("withdrawal", op.UserId);
            if (prior.Any(o => o.Status == "Held")) throw new InvalidOperationException("A withdrawal is already awaiting confirmation.");
            var used = prior.Where(o => o.CreatedUtc.Date == DateTime.UtcNow.Date).Sum(o => (decimal)o.Atomic);
            if (used + op.Atomic > cfg.ToAtomic(cfg.Transfers.DailyWithdrawalLimit)) throw new InvalidOperationException("Daily withdrawal limit reached.");
            if (op.Atomic < cfg.ToAtomic(cfg.Transfers.MinimumWithdrawal) || op.Atomic > cfg.ToAtomic(cfg.Transfers.MaximumWithdrawal))
                throw new InvalidOperationException("Withdrawal is outside the configured limits.");
            var account = _db.GetPlayerAccount(op.UserId) ?? throw new InvalidOperationException("Missing account.");
            _db.Atomic(tx =>
            {
                tx.Wallet(account, checked(account.WalletAtomic - op.Atomic));
                tx.Insert(op);
                tx.Insert(new("outgoing:" + op.FullHash, "submission", op.UserId, "Reserved", DateTime.UtcNow, op.CurrencyId, FullHash: op.FullHash));
                tx.Ledger("hold:" + op.Id, account.Id, null, op.Atomic, "withdrawal_hold", op.FullHash, account.Name);
                return true;
            });
        }
    }
}
