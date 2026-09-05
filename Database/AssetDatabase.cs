using MySql.Data.MySqlClient;
using ArkoviaEconomy.Models;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureAssetSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());

        creator.EnsureTableStructure(new SqlTable("ArkoviaAssets",
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("AssetType", MySqlDbType.VarChar, 32),
            new SqlColumn("Name", MySqlDbType.VarChar, 128),
            new SqlColumn("OwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("OwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("MetadataJson", MySqlDbType.Text),
            new SqlColumn("Version", MySqlDbType.Int32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaAssetTransfers",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("TransferKey", MySqlDbType.VarChar, 160) { Unique = true },
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64),
            new SqlColumn("FromOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("FromOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("ToOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("ToOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("Reason", MySqlDbType.VarChar, 160),
            new SqlColumn("Actor", MySqlDbType.VarChar, 128),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaTowns",
            new SqlColumn("TownId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64) { Unique = true },
            new SqlColumn("Name", MySqlDbType.VarChar, 128) { Unique = true },
            new SqlColumn("FounderUserId", MySqlDbType.Int32),
            new SqlColumn("TreasuryAccountId", MySqlDbType.Int64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaTownMembers",
            new SqlColumn("MembershipKey", MySqlDbType.VarChar, 140) { Primary = true },
            new SqlColumn("TownId", MySqlDbType.VarChar, 64),
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("Role", MySqlDbType.VarChar, 32),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("JoinedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaProperties",
            new SqlColumn("PropertyId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64) { Unique = true },
            new SqlColumn("PropertyType", MySqlDbType.VarChar, 32),
            new SqlColumn("WorldKey", MySqlDbType.VarChar, 128),
            new SqlColumn("RegionName", MySqlDbType.VarChar, 128),
            new SqlColumn("TownId", MySqlDbType.VarChar, 64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));
    }

    public static string NewAssetId()
        => "ARK-ASSET-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    public ArkoviaAsset CreateAsset(
        string assetType,
        string name,
        string ownerType,
        string ownerId,
        string metadataJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(assetType)) throw new InvalidOperationException("Asset type is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Asset name is required.");
        if (string.IsNullOrWhiteSpace(ownerType) || string.IsNullOrWhiteSpace(ownerId))
            throw new InvalidOperationException("Asset owner is required.");

        var id = NewAssetId();
        var now = DateTime.UtcNow.ToString("O");
        _db.Query(
            "INSERT INTO ArkoviaAssets (AssetId,AssetType,Name,OwnerType,OwnerId,Status,MetadataJson,Version,CreatedUtc,UpdatedUtc) " +
            "VALUES (@0,@1,@2,@3,@4,'active',@5,1,@6,@6)",
            id, assetType.Trim().ToLowerInvariant(), name.Trim(), ownerType.Trim().ToLowerInvariant(), ownerId.Trim(), metadataJson, now);
        return GetAsset(id)!;
    }

    public ArkoviaAsset? GetAsset(string assetId)
    {
        using var r = _db.QueryReader("SELECT * FROM ArkoviaAssets WHERE AssetId=@0", assetId);
        if (!r.Read()) return null;
        return ReadAsset(r);
    }

    public bool AssetTransferExists(string transferKey)
    {
        using var r = _db.QueryReader("SELECT Id FROM ArkoviaAssetTransfers WHERE TransferKey=@0", transferKey);
        return r.Read();
    }

    public bool TransferAsset(
        string assetId,
        string expectedOwnerType,
        string expectedOwnerId,
        string toOwnerType,
        string toOwnerId,
        string transferKey,
        string reason,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(transferKey)) throw new InvalidOperationException("Transfer key is required.");
        if (AssetTransferExists(transferKey)) return false;

        var asset = GetAsset(assetId) ?? throw new InvalidOperationException("Asset was not found.");
        if (!string.Equals(asset.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Asset is not transferable in its current state.");
        if (!string.Equals(asset.OwnerType, expectedOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, expectedOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Asset ownership changed; refresh and retry.");
        if (string.IsNullOrWhiteSpace(toOwnerType) || string.IsNullOrWhiteSpace(toOwnerId))
            throw new InvalidOperationException("Destination owner is required.");

        Atomic(unit =>
        {
            var now = DateTime.UtcNow.ToString("O");
            if (unit.Execute(
                "UPDATE ArkoviaAssets SET OwnerType=@p0,OwnerId=@p1,Version=Version+1,UpdatedUtc=@p2 " +
                "WHERE AssetId=@p3 AND OwnerType=@p4 AND OwnerId=@p5 AND Version=@p6 AND Status='active'",
                toOwnerType.Trim().ToLowerInvariant(), toOwnerId.Trim(), now, asset.AssetId,
                asset.OwnerType, asset.OwnerId, asset.Version) != 1)
                throw new InvalidOperationException("Asset changed during transfer; no ownership was changed. Retry.");

            unit.Execute(
                "INSERT INTO ArkoviaAssetTransfers " +
                "(TransferKey,AssetId,FromOwnerType,FromOwnerId,ToOwnerType,ToOwnerId,Reason,Actor,CreatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                transferKey, asset.AssetId, asset.OwnerType, asset.OwnerId,
                toOwnerType.Trim().ToLowerInvariant(), toOwnerId.Trim(), reason, actor, now);
            return 0;
        });
        return true;
    }

    public List<AssetTransferRecord> GetAssetTransfers(string assetId, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var result = new List<AssetTransferRecord>();
        using var r = _db.QueryReader(
            $"SELECT * FROM ArkoviaAssetTransfers WHERE AssetId=@0 ORDER BY Id DESC LIMIT {limit}", assetId);
        while (r.Read())
        {
            result.Add(new AssetTransferRecord(
                r.Get<long>("Id"), r.Get<string>("TransferKey"), r.Get<string>("AssetId"),
                r.Get<string>("FromOwnerType"), r.Get<string>("FromOwnerId"),
                r.Get<string>("ToOwnerType"), r.Get<string>("ToOwnerId"),
                r.Get<string>("Reason"), r.Get<string>("Actor"), DateTime.Parse(r.Get<string>("CreatedUtc"))));
        }
        return result;
    }

    private static ArkoviaAsset ReadAsset(System.Data.IDataReader r)
        => new(
            r.Get<string>("AssetId"), r.Get<string>("AssetType"), r.Get<string>("Name"),
            r.Get<string>("OwnerType"), r.Get<string>("OwnerId"), r.Get<string>("Status"),
            r.Get<string>("MetadataJson"), r.Get<int>("Version"),
            DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc")));
}
