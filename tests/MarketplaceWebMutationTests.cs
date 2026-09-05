using System.Runtime.CompilerServices;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class MarketplaceWebMutationTests
{
    [ModuleInitializer]
    public static void Initialize() => Run();

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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-web-mutation-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();
        var cfg = new EconomyConfig();
        var economy = new EconomyService(db, () => cfg);
        var towns = new TownService(db, economy);
        var market = new MarketplaceService(db, economy, () => cfg);
        var mutations = new MarketplaceWebMutationService(db, market, towns);

        var sellerAccountId = db.CreateAccount(101, "player", "Web Seller", 0);
        var buyerAccountId = db.CreateAccount(202, "player", "Web Buyer", 500);
        db.CreateOrConfirmWebAccountLink(101, "Web Seller", "web:seller-101");
        db.CreateOrConfirmWebAccountLink(202, "Web Buyer", "web:buyer-202");

        var asset = db.CreateAsset("collectible", "Web Relic", "player", "101");
        var listing = db.CreateMarketplaceListing(asset.AssetId, "player", "101", sellerAccountId, 200);
        var bought = mutations.Buy("web:buyer-202", listing.ListingId, "buy-operation-0001");
        Equal("completed", bought.Status);
        Equal("buy", bought.Kind);
        Equal(300L, db.GetAccountById(buyerAccountId)!.WalletAtomic);
        Equal(200L, db.GetAccountById(sellerAccountId)!.WalletAtomic);
        Equal("202", db.GetAsset(asset.AssetId)!.OwnerId);

        // Same idempotency key replays the completed result without charging twice.
        var replay = mutations.Buy("web:buyer-202", listing.ListingId, "buy-operation-0001");
        Equal(bought.ResultId, replay.ResultId);
        Equal(300L, db.GetAccountById(buyerAccountId)!.WalletAtomic);
        Equal(200L, db.GetAccountById(sellerAccountId)!.WalletAtomic);

        // A key cannot be rebound to another request.
        var secondAsset = db.CreateAsset("collectible", "Second Web Relic", "player", "101");
        var secondListing = db.CreateMarketplaceListing(secondAsset.AssetId, "player", "101", sellerAccountId, 50);
        Reject(() => mutations.Buy("web:buyer-202", secondListing.ListingId, "buy-operation-0001"));

        // Seller cancellation is idempotent and unlocks the listed asset.
        var cancelAsset = db.CreateAsset("collectible", "Web Cancel", "player", "101");
        var cancelListing = db.CreateMarketplaceListing(cancelAsset.AssetId, "player", "101", sellerAccountId, 25);
        var cancelled = mutations.Cancel("web:seller-101", cancelListing.ListingId, "cancel-operation-0001");
        Equal("completed", cancelled.Status);
        Equal("cancelled", db.GetMarketplaceListing(cancelListing.ListingId)!.Status);
        Equal("active", db.GetAsset(cancelAsset.AssetId)!.Status);
        Equal(cancelled.ResultId, mutations.Cancel("web:seller-101", cancelListing.ListingId, "cancel-operation-0001").ResultId);

        // A linked buyer cannot cancel another player's listing.
        Reject(() => mutations.Cancel("web:buyer-202", secondListing.ListingId, "cancel-operation-0002"));
        Equal("active", db.GetMarketplaceListing(secondListing.ListingId)!.Status);
        Equal("failed", db.GetMarketplaceWebOperation("cancel-operation-0002")!.Status);

        Reject(() => mutations.Buy("web:missing-user", secondListing.ListingId, "buy-operation-0003"));
        Reject(() => mutations.Buy("web:buyer-202", secondListing.ListingId, "short"));

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
