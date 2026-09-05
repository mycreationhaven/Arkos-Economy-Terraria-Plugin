using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Core;

public sealed partial class EconomyService
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
        if (feeAtomic < 0) throw new InvalidOperationException("Fee cannot be negative.");
        lock (_gate)
        {
            from = _db.GetAccountById(from.Id) ?? throw new InvalidOperationException("Sender account was not found.");
            to = _db.GetAccountById(to.Id) ?? throw new InvalidOperationException("Recipient account was not found.");
            if (from.Frozen || to.Frozen) throw new InvalidOperationException("One of the accounts is frozen.");
            if (from.Id == to.Id) throw new InvalidOperationException("Sender and recipient must be different accounts.");

            var total = checked(amountAtomic + feeAtomic);
            if (from.WalletAtomic < total) throw new InvalidOperationException("Insufficient wallet balance.");
            if (to.AccountType == "player" && checked(to.WalletAtomic + amountAtomic) > _config().ToAtomic(_config().MaximumPlayerBalance))
                throw new InvalidOperationException("Recipient balance limit would be exceeded.");

            var changes = new List<EconomyDatabase.AccountBalanceChange>
            {
                new(from.Id, from.WalletAtomic, from.WalletAtomic - total, from.BankAtomic, from.BankAtomic),
                new(to.Id, to.WalletAtomic, checked(to.WalletAtomic + amountAtomic), to.BankAtomic, to.BankAtomic)
            };

            var mainExternal = Guid.NewGuid().ToString("N");
            var writes = new List<EconomyDatabase.LedgerWrite>
            {
                new(mainExternal, from.Id, to.Id, amountAtomic, type, refType, refId, description, actor)
            };

            if (feeAtomic > 0 && _config().ReturnServerFeesToTreasury)
            {
                var treasury = GetTreasury();
                treasury = _db.GetAccountById(treasury.Id) ?? throw new InvalidOperationException("Treasury account was not found.");
                if (treasury.Id == from.Id || treasury.Id == to.Id)
                    throw new InvalidOperationException("Fee settlement cannot reuse the sender or recipient account.");

                changes.Add(new EconomyDatabase.AccountBalanceChange(
                    treasury.Id,
                    treasury.WalletAtomic,
                    checked(treasury.WalletAtomic + feeAtomic),
                    treasury.BankAtomic,
                    treasury.BankAtomic));
                writes.Add(new EconomyDatabase.LedgerWrite(
                    Guid.NewGuid().ToString("N"),
                    from.Id,
                    treasury.Id,
                    feeAtomic,
                    "fee",
                    refType,
                    refId,
                    $"Fee: {description}",
                    actor));
            }

            _db.CommitAccountSettlement(changes, writes);
            return _db.GetTransactions(to.Id, 1)[0];
        }
    }

    public void CreditTreasury(long atomic, string externalId, string refId, string description)
    {
        if (atomic <= 0) return;
        if (string.IsNullOrWhiteSpace(externalId)) throw new InvalidOperationException("External transaction ID is required.");
        lock (_gate)
        {
            if (_db.TransactionExists(externalId)) return;
            var t = GetTreasury();
            t = _db.GetAccountById(t.Id) ?? throw new InvalidOperationException("Treasury account was not found.");
            _db.CommitAccountSettlement(
                new[]
                {
                    new EconomyDatabase.AccountBalanceChange(
                        t.Id,
                        t.WalletAtomic,
                        checked(t.WalletAtomic + atomic),
                        t.BankAtomic,
                        t.BankAtomic)
                },
                new[]
                {
                    new EconomyDatabase.LedgerWrite(
                        externalId,
                        null,
                        t.Id,
                        atomic,
                        "blockchain_funding",
                        "arkovia_ledger",
                        refId,
                        description,
                        "ArkoviaNetwork")
                });
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
            _db.CommitWalletMovement(new[] { (account.Id, account.WalletAtomic, next) },
                deltaAtomic < 0 ? account.Id : null, deltaAtomic > 0 ? account.Id : null,
                Math.Abs(deltaAtomic), "admin_adjustment", "admin", "manual", reason, actor);
        }
    }

    public void Deposit(EconomyAccount account, long atomic, string actor)
    {
        if (atomic <= 0) throw new InvalidOperationException("Amount must be positive.");
        lock (_gate)
        {
            account = _db.GetAccountById(account.Id) ?? throw new InvalidOperationException("Account was not found.");
            if (account.Frozen) throw new InvalidOperationException("Account is frozen.");
            var fee = Percent(atomic, _config().Banking.DepositFeePercent);
            var totalDebit = checked(atomic + fee);
            if (account.WalletAtomic < totalDebit) throw new InvalidOperationException("Insufficient wallet balance.");

            var changes = new List<EconomyDatabase.AccountBalanceChange>
            {
                new(
                    account.Id,
                    account.WalletAtomic,
                    account.WalletAtomic - totalDebit,
                    account.BankAtomic,
                    checked(account.BankAtomic + atomic))
            };
            var writes = new List<EconomyDatabase.LedgerWrite>
            {
                new(
                    Guid.NewGuid().ToString("N"),
                    account.Id,
                    account.Id,
                    atomic,
                    "bank_deposit",
                    "bank",
                    "deposit",
                    "Wallet to bank",
                    actor)
            };

            if (fee > 0 && _config().ReturnServerFeesToTreasury)
            {
                var treasury = GetTreasury();
                treasury = _db.GetAccountById(treasury.Id) ?? throw new InvalidOperationException("Treasury account was not found.");
                if (treasury.Id == account.Id) throw new InvalidOperationException("Player account cannot be the treasury account.");
                changes.Add(new EconomyDatabase.AccountBalanceChange(
                    treasury.Id,
                    treasury.WalletAtomic,
                    checked(treasury.WalletAtomic + fee),
                    treasury.BankAtomic,
                    treasury.BankAtomic));
                writes.Add(new EconomyDatabase.LedgerWrite(
                    Guid.NewGuid().ToString("N"),
                    account.Id,
                    treasury.Id,
                    fee,
                    "fee",
                    "bank",
                    "fee",
                    "deposit fee",
                    actor));
            }

            _db.CommitAccountSettlement(changes, writes);
        }
    }

    public void Withdraw(EconomyAccount account, long atomic, string actor)
    {
        if (atomic <= 0) throw new InvalidOperationException("Amount must be positive.");
        lock (_gate)
        {
            account = _db.GetAccountById(account.Id) ?? throw new InvalidOperationException("Account was not found.");
            if (account.Frozen) throw new InvalidOperationException("Account is frozen.");
            var fee = Percent(atomic, _config().Banking.WithdrawalFeePercent);
            var totalDebit = checked(atomic + fee);
            if (account.BankAtomic < totalDebit) throw new InvalidOperationException("Insufficient bank balance.");
            if (account.AccountType == "player" && checked(account.WalletAtomic + atomic) > _config().ToAtomic(_config().MaximumPlayerBalance))
                throw new InvalidOperationException("Withdrawal would exceed the player's maximum wallet balance.");

            var changes = new List<EconomyDatabase.AccountBalanceChange>
            {
                new(
                    account.Id,
                    account.WalletAtomic,
                    checked(account.WalletAtomic + atomic),
                    account.BankAtomic,
                    account.BankAtomic - totalDebit)
            };
            var writes = new List<EconomyDatabase.LedgerWrite>
            {
                new(
                    Guid.NewGuid().ToString("N"),
                    account.Id,
                    account.Id,
                    atomic,
                    "bank_withdrawal",
                    "bank",
                    "withdraw",
                    "Bank to wallet",
                    actor)
            };

            if (fee > 0 && _config().ReturnServerFeesToTreasury)
            {
                var treasury = GetTreasury();
                treasury = _db.GetAccountById(treasury.Id) ?? throw new InvalidOperationException("Treasury account was not found.");
                if (treasury.Id == account.Id) throw new InvalidOperationException("Player account cannot be the treasury account.");
                changes.Add(new EconomyDatabase.AccountBalanceChange(
                    treasury.Id,
                    treasury.WalletAtomic,
                    checked(treasury.WalletAtomic + fee),
                    treasury.BankAtomic,
                    treasury.BankAtomic));
                writes.Add(new EconomyDatabase.LedgerWrite(
                    Guid.NewGuid().ToString("N"),
                    account.Id,
                    treasury.Id,
                    fee,
                    "fee",
                    "bank",
                    "fee",
                    "withdrawal fee",
                    actor));
            }

            _db.CommitAccountSettlement(changes, writes);
        }
    }

    public long ApplyGameplayReward(
        EconomyAccount player,
        long amountAtomic,
        string eventType,
        string referenceId,
        string description,
        string actor)
    {
        if (amountAtomic <= 0)
            throw new InvalidOperationException(
                "Gameplay reward must be positive.");

        lock (_gate)
        {
            player = _db.GetAccountById(player.Id)
                ?? throw new InvalidOperationException(
                    "Player economy account was not found.");

            if (player.Frozen)
                throw new InvalidOperationException(
                    "Player economy account is frozen.");

            var treasury = GetTreasury();

            treasury = _db.GetAccountById(treasury.Id)
                ?? throw new InvalidOperationException(
                    "Treasury account was not found.");

            if (treasury.WalletAtomic < amountAtomic)
                throw new InvalidOperationException(
                    "The Terraria Treasury does not have enough funds " +
                    "for this gameplay reward.");

            var maximum =
                _config().ToAtomic(
                    _config().MaximumPlayerBalance);

            if (checked(player.WalletAtomic + amountAtomic) > maximum)
                throw new InvalidOperationException(
                    "Gameplay reward would exceed the player's " +
                    "maximum wallet balance.");

            _db.CommitAccountSettlement(
                new[]
                {
                    new EconomyDatabase.AccountBalanceChange(
                        treasury.Id,
                        treasury.WalletAtomic,
                        treasury.WalletAtomic - amountAtomic,
                        treasury.BankAtomic,
                        treasury.BankAtomic),
                    new EconomyDatabase.AccountBalanceChange(
                        player.Id,
                        player.WalletAtomic,
                        checked(player.WalletAtomic + amountAtomic),
                        player.BankAtomic,
                        player.BankAtomic)
                },
                new[]
                {
                    new EconomyDatabase.LedgerWrite(
                        Guid.NewGuid().ToString("N"),
                        treasury.Id,
                        player.Id,
                        amountAtomic,
                        "gameplay_reward",
                        eventType,
                        referenceId,
                        description,
                        actor)
                });

            return amountAtomic;
        }
    }

    public long ApplyPercentageDeathLoss(EconomyAccount player, decimal percent,
        long protectedBalanceAtomic, string referenceId, string actor)
    {
        if (percent is < 0 or > 100)
            throw new InvalidOperationException("Death percentage must be 0-100.");
        lock (_gate)
        {
            player = _db.GetAccountById(player.Id)
                ?? throw new InvalidOperationException("Player account was not found.");
            // Round down so fractional atomic units never increase the configured penalty.
            var requested = checked((long)Math.Floor(player.WalletAtomic * percent / 100m));
            return requested <= 0 ? 0 : ApplyGameplayLoss(player, requested,
                protectedBalanceAtomic, "player_death", referenceId,
                $"Death penalty ({percent}%) to Terraria Treasury", actor);
        }
    }

    public long ApplyGameplayLoss(
        EconomyAccount player,
        long requestedAtomic,
        long protectedBalanceAtomic,
        string eventType,
        string referenceId,
        string description,
        string actor)
    {
        if (requestedAtomic <= 0)
            throw new InvalidOperationException(
                "Gameplay loss must be positive.");

        if (protectedBalanceAtomic < 0)
            throw new InvalidOperationException(
                "Protected balance cannot be negative.");

        lock (_gate)
        {
            player = _db.GetAccountById(player.Id)
                ?? throw new InvalidOperationException(
                    "Player economy account was not found.");

            if (player.Frozen)
                throw new InvalidOperationException(
                    "Player economy account is frozen.");

            var available =
                Math.Max(
                    0L,
                    player.WalletAtomic - protectedBalanceAtomic);

            var actualLoss =
                Math.Min(
                    requestedAtomic,
                    available);

            if (actualLoss <= 0)
                return 0;

            var treasury = GetTreasury();

            treasury = _db.GetAccountById(treasury.Id)
                ?? throw new InvalidOperationException(
                    "Treasury account was not found.");

            var nextTreasuryBalance = checked(treasury.WalletAtomic + actualLoss);
            _db.CommitWalletMovement(new[]
                {
                    (player.Id, player.WalletAtomic, player.WalletAtomic - actualLoss),
                    (treasury.Id, treasury.WalletAtomic, nextTreasuryBalance)
                }, player.Id, treasury.Id, actualLoss, "gameplay_loss",
                eventType, referenceId, description, actor);

            return actualLoss;
        }
    }

    public GameplayPvpResult ApplyGameplayPvpLoss(
        EconomyAccount defeated,
        EconomyAccount winner,
        long requestedAtomic,
        long protectedBalanceAtomic,
        decimal winnerPercent,
        string referenceId,
        string description,
        string actor)
    {
        if (requestedAtomic <= 0)
            throw new InvalidOperationException(
                "PvP loss must be positive.");

        if (protectedBalanceAtomic < 0)
            throw new InvalidOperationException(
                "Protected balance cannot be negative.");

        if (winnerPercent is < 0 or > 100)
            throw new InvalidOperationException(
                "PvP winner percentage must be between 0 and 100.");

        if (defeated.Id == winner.Id)
            throw new InvalidOperationException(
                "A player cannot receive their own PvP loss.");

        lock (_gate)
        {
            defeated = _db.GetAccountById(defeated.Id)
                ?? throw new InvalidOperationException(
                    "Defeated player account was not found.");

            winner = _db.GetAccountById(winner.Id)
                ?? throw new InvalidOperationException(
                    "Winner account was not found.");

            if (defeated.Frozen || winner.Frozen)
                throw new InvalidOperationException(
                    "One of the player economy accounts is frozen.");

            var available =
                Math.Max(
                    0L,
                    defeated.WalletAtomic - protectedBalanceAtomic);

            var actualLoss =
                Math.Min(
                    requestedAtomic,
                    available);

            if (actualLoss <= 0)
            {
                return new GameplayPvpResult(
                    0,
                    0,
                    0);
            }

            var winnerAmount =
                Percent(
                    actualLoss,
                    winnerPercent);

            // Use the remainder for treasury so atomic units
            // always conserve exactly.
            var treasuryAmount =
                actualLoss - winnerAmount;

            var maximum =
                _config().ToAtomic(
                    _config().MaximumPlayerBalance);

            if (checked(winner.WalletAtomic + winnerAmount) > maximum)
            {
                throw new InvalidOperationException(
                    "PvP award would exceed the winner's " +
                    "maximum wallet balance.");
            }

            var treasury = GetTreasury();

            treasury = _db.GetAccountById(treasury.Id)
                ?? throw new InvalidOperationException(
                    "Treasury account was not found.");

            var changes = new List<EconomyDatabase.AccountBalanceChange>
            {
                new(
                    defeated.Id,
                    defeated.WalletAtomic,
                    defeated.WalletAtomic - actualLoss,
                    defeated.BankAtomic,
                    defeated.BankAtomic)
            };
            var writes = new List<EconomyDatabase.LedgerWrite>();

            if (winnerAmount > 0)
            {
                changes.Add(new EconomyDatabase.AccountBalanceChange(
                    winner.Id,
                    winner.WalletAtomic,
                    checked(winner.WalletAtomic + winnerAmount),
                    winner.BankAtomic,
                    winner.BankAtomic));
                writes.Add(new EconomyDatabase.LedgerWrite(
                    Guid.NewGuid().ToString("N"),
                    defeated.Id,
                    winner.Id,
                    winnerAmount,
                    "gameplay_pvp_award",
                    "pvp",
                    referenceId,
                    description,
                    actor));
            }

            if (treasuryAmount > 0)
            {
                changes.Add(new EconomyDatabase.AccountBalanceChange(
                    treasury.Id,
                    treasury.WalletAtomic,
                    checked(treasury.WalletAtomic + treasuryAmount),
                    treasury.BankAtomic,
                    treasury.BankAtomic));
                writes.Add(new EconomyDatabase.LedgerWrite(
                    Guid.NewGuid().ToString("N"),
                    defeated.Id,
                    treasury.Id,
                    treasuryAmount,
                    "gameplay_pvp_treasury",
                    "pvp",
                    referenceId,
                    description,
                    actor));
            }

            _db.CommitAccountSettlement(changes, writes);

            return new GameplayPvpResult(
                actualLoss,
                winnerAmount,
                treasuryAmount);
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
