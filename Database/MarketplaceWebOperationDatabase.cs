using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed record MarketplaceWebOperation(
    string OperationKey,
    string WebSubject,
    int TShockUserId,
    string Kind,
    string ListingId,
    string Status,
    string ResultId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed partial class EconomyDatabase
{
    private void EnsureMarketplaceWebOperationSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaMarketplaceWebOperations",
            new SqlColumn("OperationKey", MySqlDbType.VarChar, 120) { Primary = true },
            new SqlColumn("WebSubject", MySqlDbType.VarChar, 128),
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("Kind", MySqlDbType.VarChar, 32),
            new SqlColumn("ListingId", MySqlDbType.VarChar, 64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("ResultId", MySqlDbType.VarChar, 64),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));
    }

    public MarketplaceWebOperation? GetMarketplaceWebOperation(string operationKey)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaMarketplaceWebOperations WHERE OperationKey=@0 LIMIT 1",
            operationKey);
        return r.Read() ? ReadMarketplaceWebOperation(r) : null;
    }

    public MarketplaceWebOperation CreateMarketplaceWebOperation(
        string operationKey,
        string webSubject,
        int tshockUserId,
        string kind,
        string listingId)
    {
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            unit.Execute(
                "INSERT INTO ArkoviaMarketplaceWebOperations " +
                "(OperationKey,WebSubject,TShockUserId,Kind,ListingId,Status,ResultId,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,'pending','',@p5,@p5)",
                operationKey, webSubject, tshockUserId, kind, listingId, now);
            return 0;
        });
        return GetMarketplaceWebOperation(operationKey)
            ?? throw new InvalidOperationException("Marketplace web operation did not persist.");
    }

    public MarketplaceWebOperation UpdateMarketplaceWebOperation(
        string operationKey,
        string expectedStatus,
        string nextStatus,
        string resultId = "")
    {
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceWebOperations SET Status=@p0,ResultId=@p1,UpdatedUtc=@p2 " +
                "WHERE OperationKey=@p3 AND Status=@p4",
                nextStatus, resultId, now, operationKey, expectedStatus) != 1)
                throw new InvalidOperationException("Marketplace web operation changed; retry.");
            return 0;
        });
        return GetMarketplaceWebOperation(operationKey)
            ?? throw new InvalidOperationException("Marketplace web operation was not found after update.");
    }

    private static MarketplaceWebOperation ReadMarketplaceWebOperation(QueryResult r) => new(
        r.Get<string>("OperationKey"),
        r.Get<string>("WebSubject"),
        r.Get<int>("TShockUserId"),
        r.Get<string>("Kind"),
        r.Get<string>("ListingId"),
        r.Get<string>("Status"),
        r.Get<string>("ResultId"),
        DateTime.Parse(r.Get<string>("CreatedUtc")),
        DateTime.Parse(r.Get<string>("UpdatedUtc")));
}
