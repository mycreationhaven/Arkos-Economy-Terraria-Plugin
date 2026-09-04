using MySql.Data.MySqlClient;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureVoteSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaVoteClaims",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("ClaimKey", MySqlDbType.VarChar, 200) { Unique = true },
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("AccountName", MySqlDbType.VarChar, 128),
            new SqlColumn("Provider", MySqlDbType.VarChar, 40),
            new SqlColumn("DayKey", MySqlDbType.VarChar, 10),
            new SqlColumn("CurrencyAtomic", MySqlDbType.Int64),
            new SqlColumn("ItemsJson", MySqlDbType.Text),
            new SqlColumn("GroupsJson", MySqlDbType.Text),
            new SqlColumn("Status", MySqlDbType.VarChar, 24),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("CompletedUtc", MySqlDbType.VarChar, 40)
        ));
    }

    public int CountVoteClaims(int userId, string dayKey, string? provider = null)
    {
        var sql = "SELECT COUNT(*) AS Total FROM ArkoviaVoteClaims WHERE TShockUserId=@0 AND DayKey=@1 AND Status IN ('Pending','Completed')";
        using var reader = provider is null
            ? _db.QueryReader(sql, userId, dayKey)
            : _db.QueryReader(sql + " AND Provider=@2", userId, dayKey, provider);
        return reader.Read() ? Convert.ToInt32(reader.Get<long>("Total")) : 0;
    }

    public bool TryReserveVoteClaim(string claimKey, int userId, string accountName, string provider,
        string dayKey, long currencyAtomic, string itemsJson, string groupsJson)
    {
        try
        {
            _db.Query("INSERT INTO ArkoviaVoteClaims " +
                "(ClaimKey,TShockUserId,AccountName,Provider,DayKey,CurrencyAtomic,ItemsJson,GroupsJson,Status,CreatedUtc,CompletedUtc) " +
                "VALUES (@0,@1,@2,@3,@4,@5,@6,@7,'Pending',@8,'')",
                claimKey, userId, accountName, provider, dayKey, currencyAtomic, itemsJson, groupsJson,
                DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void CompleteVoteClaim(string claimKey) => _db.Query(
        "UPDATE ArkoviaVoteClaims SET Status='Completed',CompletedUtc=@0 WHERE ClaimKey=@1 AND Status='Pending'",
        DateTime.UtcNow.ToString("O"), claimKey);

    public void ReleaseVoteClaim(string claimKey) =>
        _db.Query("DELETE FROM ArkoviaVoteClaims WHERE ClaimKey=@0 AND Status='Pending'", claimKey);
}
