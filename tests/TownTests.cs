using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class TownTests
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
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { checks++; return; }
            throw new Exception("Expected town operation rejection.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-town-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();
        var cfg = new EconomyConfig();
        var economy = new EconomyService(db, () => cfg);
        var towns = new TownService(db, economy);

        var founder = economy.GetOrCreatePlayer(5001, "Founder");
        var resident = economy.GetOrCreatePlayer(5002, "Resident");
        db.SetBalances(founder.Id, cfg.ToAtomic(100), 0);
        db.SetBalances(resident.Id, cfg.ToAtomic(50), 0);

        var town = towns.CreateTown(5001, "Bellweather");
        Equal("Bellweather", town.Name);
        Equal("mayor", db.GetTownMember(town.TownId, 5001)!.Role);
        Equal("town", db.GetAccountById(town.TreasuryAccountId)!.AccountType);
        Equal(true, town.AssetId.StartsWith("ARK-ASSET-"));
        Reject(() => towns.CreateTown(5001, "Another Town"));

        var invite = towns.Invite(town, 5001, 5002);
        Equal("pending", invite.Status);
        towns.AcceptInvite(town.TownId, 5002);
        Equal("resident", db.GetTownMember(town.TownId, 5002)!.Role);
        Reject(() => towns.AcceptInvite(town.TownId, 5002));

        towns.Deposit(town, 5002, "Resident", cfg.ToAtomic(10));
        Equal(cfg.ToAtomic(40), db.GetAccountById(resident.Id)!.WalletAtomic);
        Equal(cfg.ToAtomic(10), db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic);

        towns.Withdraw(town, 5001, "Founder", cfg.ToAtomic(4));
        Equal(cfg.ToAtomic(104), db.GetAccountById(founder.Id)!.WalletAtomic);
        Equal(cfg.ToAtomic(6), db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic);
        Reject(() => towns.Withdraw(town, 5002, "Resident", cfg.ToAtomic(1)));

        var property = db.CreateTownProperty(town.TownId, "land", "world-1", "Town Square", "Town Square");
        Equal(town.TownId, property.TownId);
        Equal("town", db.GetAsset(property.AssetId)!.OwnerType);
        Equal(town.TownId, db.GetAsset(property.AssetId)!.OwnerId);
        Reject(() => db.CreateTownProperty(town.TownId, "land", "world-1", "Town Square", "Duplicate"));

        towns.Leave(5002);
        Equal(null, db.GetTownMember(town.TownId, 5002));
        Reject(() => towns.Leave(5001));

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
