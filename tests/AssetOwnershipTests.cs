using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class AssetOwnershipTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-assets-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();

        var property = db.CreateAsset("property", "Haven House", "player", "101", "{\"kind\":\"house\"}");
        Equal(true, property.AssetId.StartsWith("ARK-ASSET-", StringComparison.Ordinal));
        Equal("property", property.AssetType);
        Equal("player", property.OwnerType);
        Equal("101", property.OwnerId);
        Equal(1, property.Version);

        Equal(true, db.TransferAsset(property.AssetId, "player", "101", "player", "202",
            "asset-test-transfer-1", "sale", "tests"));
        var transferred = db.GetAsset(property.AssetId)!;
        Equal("202", transferred.OwnerId);
        Equal(2, transferred.Version);
        Equal(1, db.GetAssetTransfers(property.AssetId).Count);

        // Replaying the same external transfer key is idempotent and does not transfer twice.
        Equal(false, db.TransferAsset(property.AssetId, "player", "101", "player", "202",
            "asset-test-transfer-1", "sale", "tests"));
        Equal(1, db.GetAssetTransfers(property.AssetId).Count);

        // Stale ownership cannot be used to move an asset.
        Reject(() => db.TransferAsset(property.AssetId, "player", "101", "player", "303",
            "asset-test-stale", "sale", "tests"));
        Equal("202", db.GetAsset(property.AssetId)!.OwnerId);
        Equal(false, db.AssetTransferExists("asset-test-stale"));

        // A transfer-log failure rolls ownership back because both writes share one transaction.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_asset_transfer BEFORE INSERT ON ArkoviaAssetTransfers BEGIN SELECT RAISE(ABORT, 'simulated asset audit failure'); END";
            command.ExecuteNonQuery();
        }
        try
        {
            db.TransferAsset(property.AssetId, "player", "202", "town", "ARK-TOWN-TEST",
                "asset-test-rollback", "town purchase", "tests");
            throw new Exception("Expected asset transfer rollback.");
        }
        catch (SqliteException)
        {
            checks++;
        }
        Equal("202", db.GetAsset(property.AssetId)!.OwnerId);
        Equal(2, db.GetAsset(property.AssetId)!.Version);
        Equal(false, db.AssetTransferExists("asset-test-rollback"));

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
