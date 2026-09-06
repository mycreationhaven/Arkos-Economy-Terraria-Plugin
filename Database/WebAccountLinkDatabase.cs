using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed record WebAccountLink(string LinkId,int TShockUserId,string TShockAccountName,string WebSubject,string Status,DateTime LinkedUtc,DateTime UpdatedUtc);

public sealed partial class EconomyDatabase
{
    private void EnsureWebAccountLinkSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaWebAccountLinks",
            new SqlColumn("LinkId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("TShockUserId", MySqlDbType.Int32) { Unique = true },
            new SqlColumn("TShockAccountName", MySqlDbType.VarChar, 64),
            new SqlColumn("WebSubject", MySqlDbType.VarChar, 128) { Unique = true },
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("LinkedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));
    }

    public WebAccountLink? GetWebAccountLinkByUser(int tshockUserId)
    {
        using var r = _db.QueryReader("SELECT * FROM ArkoviaWebAccountLinks WHERE TShockUserId=@0 AND Status='active' LIMIT 1", tshockUserId);
        return r.Read() ? ReadWebAccountLink(r) : null;
    }

    public WebAccountLink? GetWebAccountLinkBySubject(string webSubject)
    {
        using var r = _db.QueryReader("SELECT * FROM ArkoviaWebAccountLinks WHERE WebSubject=@0 AND Status='active' LIMIT 1", webSubject);
        return r.Read() ? ReadWebAccountLink(r) : null;
    }

    public WebAccountLink RebindWebAccountLinkSubject(int tshockUserId, string webSubject)
    {
        webSubject = webSubject.Trim();
        if (tshockUserId <= 0 || webSubject.Length is < 8 or > 128 || !webSubject.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
            throw new InvalidOperationException("Invalid marketplace identity.");
        var existing = GetWebAccountLinkByUser(tshockUserId) ?? throw new InvalidOperationException("Marketplace identity was not found.");
        if (string.Equals(existing.WebSubject, webSubject, StringComparison.Ordinal)) return existing;
        var collision = GetWebAccountLinkBySubject(webSubject);
        if (collision is not null && collision.TShockUserId != tshockUserId)
            throw new InvalidOperationException("This ARKOS wallet authentication identity is already in use.");
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit => { unit.Execute("UPDATE ArkoviaWebAccountLinks SET WebSubject=@p0,UpdatedUtc=@p1 WHERE TShockUserId=@p2 AND Status='active'", webSubject, now, tshockUserId); return 0; });
        return GetWebAccountLinkByUser(tshockUserId) ?? throw new InvalidOperationException("Marketplace identity migration did not persist.");
    }

    public WebAccountLink CreateOrConfirmWebAccountLink(int tshockUserId, string tshockAccountName, string webSubject)
    {
        tshockAccountName = tshockAccountName.Trim(); webSubject = webSubject.Trim();
        if (tshockUserId <= 0 || tshockAccountName.Length is < 1 or > 64) throw new InvalidOperationException("Invalid TShock account identity.");
        if (webSubject.Length is < 8 or > 128 || !webSubject.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':')) throw new InvalidOperationException("Invalid web account subject.");
        var existingUser = GetWebAccountLinkByUser(tshockUserId);
        if (existingUser is not null) { if (!string.Equals(existingUser.WebSubject, webSubject, StringComparison.Ordinal)) throw new InvalidOperationException("This Terraria account is already linked to another web account."); return existingUser; }
        var existingSubject = GetWebAccountLinkBySubject(webSubject);
        if (existingSubject is not null) { if (existingSubject.TShockUserId != tshockUserId) throw new InvalidOperationException("This web account is already linked to another Terraria account."); return existingSubject; }
        var linkId = "ARK-WEBLINK-" + Guid.NewGuid().ToString("N").ToUpperInvariant(); var now = DateTime.UtcNow.ToString("O");
        Atomic(unit => { unit.Execute("INSERT INTO ArkoviaWebAccountLinks (LinkId,TShockUserId,TShockAccountName,WebSubject,Status,LinkedUtc,UpdatedUtc) VALUES (@p0,@p1,@p2,@p3,'active',@p4,@p4)",linkId,tshockUserId,tshockAccountName,webSubject,now); return 0; });
        return GetWebAccountLinkByUser(tshockUserId) ?? throw new InvalidOperationException("Web account link did not persist.");
    }

    private static WebAccountLink ReadWebAccountLink(QueryResult r) => new(r.Get<string>("LinkId"),r.Get<int>("TShockUserId"),r.Get<string>("TShockAccountName"),r.Get<string>("WebSubject"),r.Get<string>("Status"),DateTime.Parse(r.Get<string>("LinkedUtc")),DateTime.Parse(r.Get<string>("UpdatedUtc")));
}
