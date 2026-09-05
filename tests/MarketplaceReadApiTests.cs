using ArkoviaEconomy.Api;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class MarketplaceReadApiTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-market-read-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();
        var cfg = new EconomyConfig { CurrencySymbol = "ARKOS", Decimals = 8 };

        var sellerAccountId = db.CreateAccount(101, "player", "SellerOne", 0);
        var buyerAccountId = db.CreateAccount(202, "player", "BuyerOne", cfg.ToAtomic(100));
        var escrowAccountId = db.CreateAccount(null, "system", "Read API Escrow", 0);

        var collectible = db.CreateAsset("collectible", "Founder's Lantern", "player", "101");
        var collectibleListing = db.CreateMarketplaceListing(
            collectible.AssetId, "player", "101", sellerAccountId, cfg.ToAtomic(12.5m));

        var town = db.CreateTownBundle("Bellweather", 303);
        var property = db.CreateTownProperty(town.TownId, "house", "777", "Garden House", "Garden House");
        var propertyListing = db.CreateMarketplaceListing(
            property.AssetId, "town", town.TownId, town.TreasuryAccountId, cfg.ToAtomic(75));

        var listings = MarketplaceReadProjection.GetActiveListings(db, cfg, 100);
        Equal(2, listings.Count);

        var collectibleView = listings.Single(x => x.ListingId == collectibleListing.ListingId);
        Equal("collectible", collectibleView.AssetType);
        Equal("Founder's Lantern", collectibleView.AssetName);
        Equal(12.5m, collectibleView.Price);
        Equal("ARKOS", collectibleView.Currency);
        Equal("player", collectibleView.SellerType);
        Equal("SellerOne", collectibleView.SellerName);
        Equal<string?>(null, collectibleView.PropertyType);
        Equal<string?>(null, collectibleView.RegionName);

        var propertyView = listings.Single(x => x.ListingId == propertyListing.ListingId);
        Equal("property", propertyView.AssetType);
        Equal("Garden House", propertyView.AssetName);
        Equal("town", propertyView.SellerType);
        Equal("Bellweather", propertyView.SellerName);
        Equal("house", propertyView.PropertyType);
        Equal("Garden House", propertyView.RegionName);

        var held = db.ReserveMarketplaceListing(
            collectibleListing.ListingId, "player", "202", buyerAccountId, escrowAccountId,
            "read-api-reserve-1", TimeSpan.FromMinutes(10), "tests");
        Equal("held", held.Status);
        Equal<MarketplaceListingView?>(null,
            MarketplaceReadProjection.GetActiveListing(db, cfg, collectibleListing.ListingId));
        Equal(1, MarketplaceReadProjection.GetActiveListings(db, cfg, 100).Count);

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
