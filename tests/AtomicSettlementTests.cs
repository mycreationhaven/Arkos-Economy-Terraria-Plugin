using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class AtomicSettlementTests
{
    public static int Run()
    {
        var checks = 0;
        void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Expected {expected}, got {actual}");
            checks++;
        }

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-atomic-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();

        var aId = db.CreateAccount(1001, "player", "AtomicA", 1_000);
        var bId = db.CreateAccount(1002, "player", "AtomicB", 100);
        var a = db.GetAccountById(aId)!;
        var b = db.GetAccountById(bId)!;

        db.CommitAccountSettlement(
            new[]
            {
                new EconomyDatabase.AccountBalanceChange(a.Id, a.WalletAtomic, 750, a.BankAtomic, a.BankAtomic),
                new EconomyDatabase.AccountBalanceChange(b.Id, b.WalletAtomic, 350, b.BankAtomic, b.BankAtomic)
            },
            new[]
            {
                new EconomyDatabase.LedgerWrite("atomic-success", a.Id, b.Id, 250,
                    "test_transfer", "test", "success", "Atomic settlement test", "tests")
            });

        Equal(750L, db.GetAccountById(a.Id)!.WalletAtomic);
        Equal(350L, db.GetAccountById(b.Id)!.WalletAtomic);
        Equal(true, db.TransactionExists("atomic-success"));

        a = db.GetAccountById(a.Id)!;
        b = db.GetAccountById(b.Id)!;
        try
        {
            db.CommitAccountSettlement(
                new[]
                {
                    new EconomyDatabase.AccountBalanceChange(a.Id, a.WalletAtomic, 700, a.BankAtomic, a.BankAtomic),
                    new EconomyDatabase.AccountBalanceChange(b.Id, b.WalletAtomic, 400, b.BankAtomic, b.BankAtomic)
                },
                new[]
                {
                    // Deliberately duplicate the unique ExternalId so the ledger insert fails.
                    new EconomyDatabase.LedgerWrite("atomic-success", a.Id, b.Id, 50,
                        "test_transfer", "test", "rollback", "Must rollback", "tests")
                });
            throw new Exception("Expected duplicate-ledger settlement failure.");
        }
        catch (SqliteException)
        {
            checks++;
        }

        Equal(750L, db.GetAccountById(a.Id)!.WalletAtomic);
        Equal(350L, db.GetAccountById(b.Id)!.WalletAtomic);

        // Stale before-values must abort the whole settlement before any ledger row is committed.
        try
        {
            db.CommitAccountSettlement(
                new[]
                {
                    new EconomyDatabase.AccountBalanceChange(a.Id, 999_999, 700, a.BankAtomic, a.BankAtomic)
                },
                new[]
                {
                    new EconomyDatabase.LedgerWrite("atomic-stale", a.Id, b.Id, 50,
                        "test_transfer", "test", "stale", "Must reject stale balance", "tests")
                });
            throw new Exception("Expected stale-balance settlement rejection.");
        }
        catch (InvalidOperationException)
        {
            checks++;
        }

        Equal(750L, db.GetAccountById(a.Id)!.WalletAtomic);
        Equal(false, db.TransactionExists("atomic-stale"));

        // Wallet and bank balances are committed together.
        a = db.GetAccountById(a.Id)!;
        db.CommitAccountSettlement(
            new[]
            {
                new EconomyDatabase.AccountBalanceChange(a.Id, a.WalletAtomic, 650, a.BankAtomic, 100)
            },
            new[]
            {
                new EconomyDatabase.LedgerWrite("atomic-bank", a.Id, a.Id, 100,
                    "test_bank", "test", "bank", "Wallet to bank", "tests")
            });
        Equal(650L, db.GetAccountById(a.Id)!.WalletAtomic);
        Equal(100L, db.GetAccountById(a.Id)!.BankAtomic);

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
