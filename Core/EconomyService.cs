using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Core;

public sealed class EconomyService
{
    public const string TreasuryName = "Terraria Treasury";
    private readonly EconomyDatabase _db;
    private readonly Func<EconomyConfig> _config;
    private readonly object _gate = new();

    public EconomyService(EconomyDatabase db, Func<EconomyConfig> config) { _db = db; _config = config; }

    public EconomyAccount GetOrCreatePlayer(int userId, string name)
    {
        lock (_gate)
        {
            var existing = _db.GetPlayerAccount(userId);
            if (existing is not null) return existing;
            var id = _db.CreateAccount(userId, "player", name, _config().ToAtomic(_config().StartingBalance));
            return _db.GetAccountById(id)!;
        }
    }

    public EconomyAccount GetTreasury()
    {
        lock (_gate)
        {
            var t = _db.GetSystemAccount(TreasuryName);
            if (t is not null) return t;
            var id = _db.CreateAccount(null, "system", TreasuryName, 0);
            return _db.GetAccountById(id)!;
        }
    }

    public LedgerTransaction Transfer(EconomyAccount from, EconomyAccount to, long amountAtomic, string type, string refType, string refId, string description, string actor, long feeAtomic = 0)
    {
        if (amountAtomic <= 0) throw new InvalidOperationException("Amount must be positive.");
        lock (_gate)
        {
            from = _db.GetAccountById(from.Id)!;
            to = _db.GetAccountById(to.Id)!;
            if (from.Frozen || to.Frozen) throw new InvalidOperationException("One of the accounts is frozen.");
            var total = checked(amountAtomic + feeAtomic);
            if (from.WalletAtomic < total) throw new InvalidOperationException("Insufficient wallet balance.");
            if (to.AccountType == "player" && checked(to.WalletAtomic + amountAtomic) > _config().ToAtomic(_config().MaximumPlayerBalance)) throw new InvalidOperationException("Recipient balance limit would be exceeded.");

            _db.SetBalances(from.Id, from.WalletAtomic - total, from.BankAtomic);
            _db.SetBalances(to.Id, checked(to.WalletAtomic + amountAtomic), to.BankAtomic);
            var external = Guid.NewGuid().ToString("N");
            _db.InsertTransaction(external, from.Id, to.Id, amountAtomic, type, refType, refId, description, actor);
            if (feeAtomic > 0 && _config().ReturnServerFeesToTreasury)
            {
                var treasury = GetTreasury();
                treasury = _db.GetAccountById(treasury.Id)!;
                _db.SetBalances(treasury.Id, checked(treasury.WalletAtomic + feeAtomic), treasury.BankAtomic);
                _db.InsertTransaction(Guid.NewGuid().ToString("N"), from.Id, treasury.Id, feeAtomic, "fee", refType, refId, $"Fee: {description}", actor);
            }
            return _db.GetTransactions(to.Id, 1)[0];
        }
    }

    public void CreditTreasury(long atomic, string externalId, string refId, string description)
    {
        if (atomic <= 0) return;
        lock (_gate)
        {
            if (_db.TransactionExists(externalId)) return;
            var t = GetTreasury();
            t = _db.GetAccountById(t.Id)!;
            _db.SetBalances(t.Id, checked(t.WalletAtomic + atomic), t.BankAtomic);
            _db.InsertTransaction(externalId, null, t.Id, atomic, "blockchain_funding", "arkovia_ledger", refId, description, "ArkoviaNetwork");
        }
    }

    public void AdminAdjust(EconomyAccount account, long deltaAtomic, string reason, string actor)
    {
        if (deltaAtomic == 0) throw new InvalidOperationException("Adjustment cannot be zero.");
        lock (_gate)
        {
            account = _db.GetAccountById(account.Id)!;
            var next = checked(account.WalletAtomic + deltaAtomic);
            if (next < 0) throw new InvalidOperationException("Adjustment would make the balance negative.");
            _db.SetBalances(account.Id, next, account.BankAtomic);
            _db.InsertTransaction(Guid.NewGuid().ToString("N"), deltaAtomic < 0 ? account.Id : null, deltaAtomic > 0 ? account.Id : null, Math.Abs(deltaAtomic), "admin_adjustment", "admin", "manual", reason, actor);
        }
    }

    public void Deposit(EconomyAccount account, long atomic, string actor)
    {
        if (atomic <= 0) throw new InvalidOperationException("Amount must be positive.");
        lock (_gate)
        {
            account = _db.GetAccountById(account.Id)!;
            if (account.Frozen) throw new InvalidOperationException("Account is frozen.");
            var fee = Percent(atomic, _config().Banking.DepositFeePercent);
            if (account.WalletAtomic < atomic + fee) throw new InvalidOperationException("Insufficient wallet balance.");
            _db.SetBalances(account.Id, account.WalletAtomic - atomic - fee, checked(account.BankAtomic + atomic));
            _db.InsertTransaction(Guid.NewGuid().ToString("N"), account.Id, account.Id, atomic, "bank_deposit", "bank", "deposit", "Wallet to bank", actor);
            CreditFee(account, fee, "deposit fee", actor);
        }
    }

    public void Withdraw(EconomyAccount account, long atomic, string actor)
    {
        if (atomic <= 0) throw new InvalidOperationException("Amount must be positive.");
        lock (_gate)
        {
            account = _db.GetAccountById(account.Id)!;
            var fee = Percent(atomic, _config().Banking.WithdrawalFeePercent);
            if (account.BankAtomic < atomic + fee) throw new InvalidOperationException("Insufficient bank balance.");
            _db.SetBalances(account.Id, checked(account.WalletAtomic + atomic), account.BankAtomic - atomic - fee);
            _db.InsertTransaction(Guid.NewGuid().ToString("N"), account.Id, account.Id, atomic, "bank_withdrawal", "bank", "withdraw", "Bank to wallet", actor);
            CreditFee(account, fee, "withdrawal fee", actor);
        }
    }

    public long Percent(long amount, decimal percent) => checked((long)Math.Round(amount * percent / 100m, 0, MidpointRounding.AwayFromZero));

    private void CreditFee(EconomyAccount source, long fee, string description, string actor)
    {
        if (fee <= 0 || !_config().ReturnServerFeesToTreasury) return;
        var t = GetTreasury(); t = _db.GetAccountById(t.Id)!;
        _db.SetBalances(t.Id, checked(t.WalletAtomic + fee), t.BankAtomic);
        _db.InsertTransaction(Guid.NewGuid().ToString("N"), source.Id, t.Id, fee, "fee", "bank", "fee", description, actor);
    }
}
