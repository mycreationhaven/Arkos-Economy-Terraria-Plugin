using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class TownGovernanceTests
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
            throw new Exception("Expected rejection");
        }

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-town-governance-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();

        var town = db.CreateTownBundle("Governance", 7001);
        db.CreateTownInvite(town.TownId, 7002, 7001, TimeSpan.FromHours(1));
        db.AcceptTownInvite(town.TownId, 7002);
        db.CreateTownInvite(town.TownId, 7003, 7001, TimeSpan.FromHours(1));
        db.AcceptTownInvite(town.TownId, 7003);

        db.SetTownMemberRole(town.TownId, 7002, "resident", "assistant");
        Equal("assistant", db.GetTownMember(town.TownId, 7002)!.Role);
        Reject(() => db.SetTownMemberRole(town.TownId, 7002, "resident", "assistant"));

        db.SetTownMemberRole(town.TownId, 7002, "assistant", "resident");
        Equal("resident", db.GetTownMember(town.TownId, 7002)!.Role);

        db.TransferTownLeadership(town.TownId, 7001, 7002);
        Equal("assistant", db.GetTownMember(town.TownId, 7001)!.Role);
        Equal("mayor", db.GetTownMember(town.TownId, 7002)!.Role);
        Reject(() => db.TransferTownLeadership(town.TownId, 7001, 7003));

        db.KickTownMember(town.TownId, 7003);
        Equal(null, db.GetTownMember(town.TownId, 7003));
        Reject(() => db.KickTownMember(town.TownId, 7002));

        var property = db.CreateTownProperty(town.TownId, "land", "world-1", "GovernanceRegion", "GovernanceRegion");
        Equal(1, db.GetTownProperties(town.TownId).Count);
        db.UnclaimTownProperty(town.TownId, "world-1", "GovernanceRegion");
        Equal(0, db.GetTownProperties(town.TownId).Count);
        Equal(null, db.GetPropertyByRegion("world-1", "GovernanceRegion"));
        Equal("retired", db.GetAsset(property.AssetId)!.Status);
        Reject(() => db.UnclaimTownProperty(town.TownId, "world-1", "GovernanceRegion"));

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
