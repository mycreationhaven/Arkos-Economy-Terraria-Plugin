using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class MarketplaceTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-market-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();

        var sellerId = db.CreateAccount(101, "player", "Market Seller", 1_000);
        var buyerId = db.CreateAccount(202, "player", "Market Buyer", 500);
        var escrowAccountId = db.CreateAccount(null, "system", "Marketplace Escrow Test", 0);

        var asset = db.CreateAsset("collectible", "Founder's Relic", "player", "101");
        var listing = db.CreateMarketplaceListing(asset.AssetId, "player", "101", sellerId, 200);
        Equal("active", listing.Status);
        Equal("listed", db.GetAsset(asset.AssetId)!.Status);
        Equal(2, db.GetAsset(asset.AssetId)!.Version);

        var held = db.ReserveMarketplaceListing(
            listing.ListingId, "player", "202", buyerId, escrowAccountId,
            "market-test-reserve-1", TimeSpan.FromMinutes(10), "Market Buyer");
        Equal("held", held.Status);
        Equal(300L, db.GetAccountById(buyerId)!.WalletAtomic);
        Equal(200L, db.GetAccountById(escrowAccountId)!.WalletAtomic);
        Equal("reserved", db.GetMarketplaceListing(listing.ListingId)!.Status);

        var sale = db.CompleteMarketplaceSale(listing.ListingId, held.ReservationKey, escrowAccountId, "Market Buyer");
        Equal(200L, sale.AmountAtomic);
        Equal(1_200L, db.GetAccountById(sellerId)!.WalletAtomic);
        Equal(300L, db.GetAccountById(buyerId)!.WalletAtomic);
        Equal(0L, db.GetAccountById(escrowAccountId)!.WalletAtomic);
        Equal("202", db.GetAsset(asset.AssetId)!.OwnerId);
        Equal("active", db.GetAsset(asset.AssetId)!.Status);
        Equal("completed", db.GetMarketplaceListing(listing.ListingId)!.Status);
        Equal("released", db.GetMarketplaceEscrow(held.ReservationKey)!.Status);
        Equal(1, db.GetAssetTransfers(asset.AssetId).Count);

        // Completing the same listing is idempotent and does not pay the seller twice.
        var replay = db.CompleteMarketplaceSale(listing.ListingId, held.ReservationKey, escrowAccountId, "Market Buyer");
        Equal(sale.SaleId, replay.SaleId);
        Equal(1_200L, db.GetAccountById(sellerId)!.WalletAtomic);

        // Seller cancellation unlocks an unreserved listed asset.
        var cancelAsset = db.CreateAsset("collectible", "Cancel Me", "player", "101");
        var cancelListing = db.CreateMarketplaceListing(cancelAsset.AssetId, "player", "101", sellerId, 50);
        db.CancelMarketplaceListing(cancelListing.ListingId, "player", "101");
        Equal("cancelled", db.GetMarketplaceListing(cancelListing.ListingId)!.Status);
        Equal("active", db.GetAsset(cancelAsset.AssetId)!.Status);

        // Releasing a reservation refunds the buyer and reopens the listing without unlocking the asset.
        var refundAsset = db.CreateAsset("collectible", "Refund Me", "player", "101");
        var refundListing = db.CreateMarketplaceListing(refundAsset.AssetId, "player", "101", sellerId, 75);
        var beforeRefundBuyer = db.GetAccountById(buyerId)!.WalletAtomic;
        var refundHeld = db.ReserveMarketplaceListing(
            refundListing.ListingId, "player", "202", buyerId, escrowAccountId,
            "market-test-refund-1", TimeSpan.FromMinutes(10), "Market Buyer");
        db.ReleaseMarketplaceReservation(refundListing.ListingId, refundHeld.ReservationKey, escrowAccountId, "tests");
        Equal(beforeRefundBuyer, db.GetAccountById(buyerId)!.WalletAtomic);
        Equal("active", db.GetMarketplaceListing(refundListing.ListingId)!.Status);
        Equal("listed", db.GetAsset(refundAsset.AssetId)!.Status);
        Equal("refunded", db.GetMarketplaceEscrow(refundHeld.ReservationKey)!.Status);

        // Self-purchase and stale seller cancellation are rejected.
        Reject(() => db.ReserveMarketplaceListing(
            refundListing.ListingId, "player", "101", sellerId, escrowAccountId,
            "market-test-self", TimeSpan.FromMinutes(10), "Seller"));
        Reject(() => db.CancelMarketplaceListing(refundListing.ListingId, "player", "999"));

        // If ownership audit insertion fails, funds and ownership stay escrowed/reserved for safe retry.
        var rollbackAsset = db.CreateAsset("collectible", "Rollback Me", "player", "101");
        var rollbackListing = db.CreateMarketplaceListing(rollbackAsset.AssetId, "player", "101", sellerId, 60);
        var rollbackHeld = db.ReserveMarketplaceListing(
            rollbackListing.ListingId, "player", "202", buyerId, escrowAccountId,
            "market-test-rollback-1", TimeSpan.FromMinutes(10), "Market Buyer");
        var sellerBeforeFailure = db.GetAccountById(sellerId)!.WalletAtomic;
        var escrowBeforeFailure = db.GetAccountById(escrowAccountId)!.WalletAtomic;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_market_transfer BEFORE INSERT ON ArkoviaAssetTransfers BEGIN SELECT RAISE(ABORT, 'simulated marketplace transfer failure'); END";
            command.ExecuteNonQuery();
        }
        try
        {
            db.CompleteMarketplaceSale(rollbackListing.ListingId, rollbackHeld.ReservationKey, escrowAccountId, "tests");
            throw new Exception("Expected marketplace settlement rollback.");
        }
        catch (SqliteException)
        {
            checks++;
        }
        Equal(sellerBeforeFailure, db.GetAccountById(sellerId)!.WalletAtomic);
        Equal(escrowBeforeFailure, db.GetAccountById(escrowAccountId)!.WalletAtomic);
        Equal("101", db.GetAsset(rollbackAsset.AssetId)!.OwnerId);
        Equal("listed", db.GetAsset(rollbackAsset.AssetId)!.Status);
        Equal("reserved", db.GetMarketplaceListing(rollbackListing.ListingId)!.Status);
        Equal("held", db.GetMarketplaceEscrow(rollbackHeld.ReservationKey)!.Status);

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
