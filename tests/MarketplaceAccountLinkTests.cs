using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class MarketplaceAccountLinkTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-web-link-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();
        var links = new MarketplaceAccountLinkService(db);

        var issuedAt = DateTime.UtcNow;
        var challenge = links.Issue(101, "LinkPlayer");
        Equal(6, challenge.Code.Length);
        Equal(true, challenge.Code.All(char.IsDigit));
        Equal(true, challenge.ExpiresUtc >= issuedAt.AddMinutes(4).AddSeconds(55));
        Equal(true, challenge.ExpiresUtc <= DateTime.UtcNow.AddMinutes(5).AddSeconds(5));
        Reject(() => links.Redeem("LinkPlayer", "000000" == challenge.Code ? "111111" : "000000", "web:user-101"));

        var linked = links.Redeem("LinkPlayer", challenge.Code, "web:user-101");
        Equal(101, linked.TShockUserId);
        Equal("LinkPlayer", linked.TShockAccountName);
        Equal("web:user-101", linked.WebSubject);
        Equal("active", linked.Status);
        Equal(linked.LinkId, db.GetWebAccountLinkByUser(101)!.LinkId);
        Equal(linked.LinkId, db.GetWebAccountLinkBySubject("web:user-101")!.LinkId);
        Reject(() => links.Redeem("LinkPlayer", challenge.Code, "web:user-101"));

        var repeat = links.Issue(101, "LinkPlayer");
        Equal(linked.LinkId, links.Redeem("LinkPlayer", repeat.Code, "web:user-101").LinkId);

        var other = links.Issue(202, "OtherPlayer");
        Reject(() => links.Redeem("OtherPlayer", other.Code, "web:user-101"));

        var conflict = links.Issue(101, "LinkPlayer");
        Reject(() => links.Redeem("LinkPlayer", conflict.Code, "web:another-user"));

        var locked = links.Issue(303, "LockedPlayer");
        var wrong = locked.Code == "999999" ? "888888" : "999999";
        for (var i = 0; i < 5; i++) Reject(() => links.Redeem("LockedPlayer", wrong, "web:user-303"));
        Reject(() => links.Redeem("LockedPlayer", locked.Code, "web:user-303"));

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
