using MySql.Data.MySqlClient;
using ArkoviaEconomy.Models;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureTownSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaTownInvites",
            new SqlColumn("InviteKey", MySqlDbType.VarChar, 160) { Primary = true },
            new SqlColumn("TownId", MySqlDbType.VarChar, 64),
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("InvitedByUserId", MySqlDbType.Int32),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("ExpiresUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));
    }

    public ArkoviaTown? GetTown(string townIdOrName)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaTowns WHERE TownId=@0 OR Name=@0 LIMIT 1", townIdOrName);
        return r.Read() ? ReadTown(r) : null;
    }

    public ArkoviaTown? GetTownForUser(int userId)
    {
        using var r = _db.QueryReader(
            "SELECT t.* FROM ArkoviaTowns t INNER JOIN ArkoviaTownMembers m ON t.TownId=m.TownId " +
            "WHERE m.TShockUserId=@0 AND m.Status='active' AND t.Status='active' LIMIT 1", userId);
        return r.Read() ? ReadTown(r) : null;
    }

    public TownMember? GetTownMember(string townId, int userId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaTownMembers WHERE TownId=@0 AND TShockUserId=@1 AND Status='active' LIMIT 1",
            townId, userId);
        return r.Read() ? ReadTownMember(r) : null;
    }

    public IReadOnlyList<TownMember> GetTownMembers(string townId)
    {
        var result = new List<TownMember>();
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaTownMembers WHERE TownId=@0 AND Status='active' ORDER BY JoinedUtc", townId);
        while (r.Read()) result.Add(ReadTownMember(r));
        return result;
    }

    public ArkoviaTown CreateTownBundle(string name, int founderUserId)
    {
        name = name.Trim();
        if (name.Length is < 3 or > 40)
            throw new InvalidOperationException("Town name must be between 3 and 40 characters.");
        if (GetTown(name) is not null)
            throw new InvalidOperationException("A town with that name already exists.");
        if (GetTownForUser(founderUserId) is not null)
            throw new InvalidOperationException("You already belong to a town.");

        var townId = "ARK-TOWN-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var assetId = NewAssetId();
        var treasuryName = "Town Treasury " + townId;
        var membershipKey = townId + ":" + founderUserId;
        var now = DateTime.UtcNow.ToString("O");

        Atomic(unit =>
        {
            unit.Execute(
                "INSERT INTO ArkoviaEconomyAccounts " +
                "(TShockUserId,AccountType,Name,WalletAtomic,BankAtomic,Frozen,CreatedUtc,UpdatedUtc) " +
                "VALUES (0,'town',@p0,0,0,0,@p1,@p1)", treasuryName, now);
            var treasuryId = unit.ScalarLong(
                "SELECT Id FROM ArkoviaEconomyAccounts WHERE AccountType='town' AND Name=@p0 ORDER BY Id DESC LIMIT 1",
                treasuryName);

            unit.Execute(
                "INSERT INTO ArkoviaAssets " +
                "(AssetId,AssetType,Name,OwnerType,OwnerId,Status,MetadataJson,Version,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,'town',@p1,'player',@p2,'active','{}',1,@p3,@p3)",
                assetId, name, founderUserId.ToString(), now);

            unit.Execute(
                "INSERT INTO ArkoviaTowns " +
                "(TownId,AssetId,Name,FounderUserId,TreasuryAccountId,Status,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,'active',@p5,@p5)",
                townId, assetId, name, founderUserId, treasuryId, now);

            unit.Execute(
                "INSERT INTO ArkoviaTownMembers " +
                "(MembershipKey,TownId,TShockUserId,Role,Status,JoinedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,'mayor','active',@p3,@p3)",
                membershipKey, townId, founderUserId, now);
            return 0;
        });

        return GetTown(townId) ?? throw new InvalidOperationException("Town creation did not persist.");
    }

    public TownInvite CreateTownInvite(string townId, int targetUserId, int invitedByUserId, TimeSpan lifetime)
    {
        if (GetTownForUser(targetUserId) is not null)
            throw new InvalidOperationException("That account already belongs to a town.");

        var existing = GetPendingTownInvite(townId, targetUserId);
        if (existing is not null && existing.ExpiresUtc > DateTime.UtcNow)
            return existing;

        var key = "towninvite:" + townId + ":" + targetUserId + ":" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime);
        _db.Query(
            "INSERT INTO ArkoviaTownInvites " +
            "(InviteKey,TownId,TShockUserId,InvitedByUserId,Status,ExpiresUtc,CreatedUtc,UpdatedUtc) " +
            "VALUES (@0,@1,@2,@3,'pending',@4,@5,@5)",
            key, townId, targetUserId, invitedByUserId, expires.ToString("O"), now.ToString("O"));
        return GetPendingTownInvite(townId, targetUserId)!;
    }

    public TownInvite? GetPendingTownInvite(string townId, int targetUserId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaTownInvites WHERE TownId=@0 AND TShockUserId=@1 AND Status='pending' " +
            "ORDER BY CreatedUtc DESC LIMIT 1", townId, targetUserId);
        return r.Read() ? ReadTownInvite(r) : null;
    }

    public void AcceptTownInvite(string townId, int userId)
    {
        if (GetTownForUser(userId) is not null)
            throw new InvalidOperationException("You already belong to a town.");
        var invite = GetPendingTownInvite(townId, userId)
            ?? throw new InvalidOperationException("No pending invitation was found for that town.");
        if (invite.ExpiresUtc <= DateTime.UtcNow)
            throw new InvalidOperationException("That town invitation has expired.");

        var membershipKey = townId + ":" + userId;
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaTownInvites SET Status='accepted',UpdatedUtc=@p0 " +
                "WHERE InviteKey=@p1 AND Status='pending'", now, invite.InviteKey) != 1)
                throw new InvalidOperationException("Invitation changed; retry.");
            unit.Execute(
                "INSERT INTO ArkoviaTownMembers " +
                "(MembershipKey,TownId,TShockUserId,Role,Status,JoinedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,'resident','active',@p3,@p3)",
                membershipKey, townId, userId, now);
            return 0;
        });
    }

    public void LeaveTown(string townId, int userId)
    {
        var member = GetTownMember(townId, userId)
            ?? throw new InvalidOperationException("You are not an active member of that town.");
        if (string.Equals(member.Role, "mayor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The mayor cannot leave until leadership transfer or disband is implemented.");
        if (_db.Query(
            "UPDATE ArkoviaTownMembers SET Status='left',UpdatedUtc=@0 WHERE MembershipKey=@1 AND Status='active'",
            DateTime.UtcNow.ToString("O"), member.MembershipKey) != 1)
            throw new InvalidOperationException("Membership changed; retry.");
    }

    public ArkoviaProperty? GetPropertyByRegion(string worldKey, string regionName)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaProperties WHERE WorldKey=@0 AND RegionName=@1 AND Status='active' LIMIT 1",
            worldKey, regionName);
        return r.Read() ? ReadProperty(r) : null;
    }

    public ArkoviaProperty CreateTownProperty(
        string townId,
        string propertyType,
        string worldKey,
        string regionName,
        string displayName)
    {
        if (GetPropertyByRegion(worldKey, regionName) is not null)
            throw new InvalidOperationException("That TShock region is already bound to an Arkovia property.");
        var propertyId = "ARK-PROP-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var assetId = NewAssetId();
        var now = DateTime.UtcNow.ToString("O");

        Atomic(unit =>
        {
            unit.Execute(
                "INSERT INTO ArkoviaAssets " +
                "(AssetId,AssetType,Name,OwnerType,OwnerId,Status,MetadataJson,Version,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,'town',@p3,'active','{}',1,@p4,@p4)",
                assetId, propertyType.Trim().ToLowerInvariant(), displayName.Trim(), townId, now);
            unit.Execute(
                "INSERT INTO ArkoviaProperties " +
                "(PropertyId,AssetId,PropertyType,WorldKey,RegionName,TownId,Status,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,'active',@p6,@p6)",
                propertyId, assetId, propertyType.Trim().ToLowerInvariant(), worldKey, regionName, townId, now);
            return 0;
        });
        return GetPropertyByRegion(worldKey, regionName)
            ?? throw new InvalidOperationException("Property creation did not persist.");
    }

    private static ArkoviaTown ReadTown(QueryResult r) => new(
        r.Get<string>("TownId"), r.Get<string>("AssetId"), r.Get<string>("Name"),
        r.Get<int>("FounderUserId"), r.Get<long>("TreasuryAccountId"), r.Get<string>("Status"),
        DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc")));

    private static TownMember ReadTownMember(QueryResult r) => new(
        r.Get<string>("MembershipKey"), r.Get<string>("TownId"), r.Get<int>("TShockUserId"),
        r.Get<string>("Role"), r.Get<string>("Status"), DateTime.Parse(r.Get<string>("JoinedUtc")),
        DateTime.Parse(r.Get<string>("UpdatedUtc")));

    private static TownInvite ReadTownInvite(QueryResult r) => new(
        r.Get<string>("InviteKey"), r.Get<string>("TownId"), r.Get<int>("TShockUserId"),
        r.Get<int>("InvitedByUserId"), r.Get<string>("Status"), DateTime.Parse(r.Get<string>("ExpiresUtc")),
        DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc")));

    private static ArkoviaProperty ReadProperty(QueryResult r) => new(
        r.Get<string>("PropertyId"), r.Get<string>("AssetId"), r.Get<string>("PropertyType"),
        r.Get<string>("WorldKey"), r.Get<string>("RegionName"),
        string.IsNullOrWhiteSpace(r.Get<string>("TownId")) ? null : r.Get<string>("TownId"),
        r.Get<string>("Status"), DateTime.Parse(r.Get<string>("CreatedUtc")),
        DateTime.Parse(r.Get<string>("UpdatedUtc")));
}
