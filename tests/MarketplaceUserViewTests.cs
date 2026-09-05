using ArkoviaEconomy.Api;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class MarketplaceUserViewTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-market-user-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();
        var cfg = new EconomyConfig { CurrencySymbol = "ARKOS", Decimals = 8 };
        var economy = new EconomyService(db, () => cfg);
        var market = new MarketplaceService(db, economy, () => cfg);

        var seller = economy.GetOrCreatePlayer(101, "SellerOne");
        var buyer = economy.GetOrCreatePlayer(202, "BuyerOne");
        db.SetBalances(buyer.Id, cfg.ToAtomic(100), 0);
        var asset = db.CreateAsset("collectible", "Moon Coin", "player", "101");
        var listing = market.ListPlayerAsset(101, "SellerOne", asset.AssetId, cfg.ToAtomic(15));
        var link = db.CreateOrConfirmWebAccountLink(202, "BuyerOne", "web:buyer-202");

        Equal(202, link.TShockUserId);
        Equal(0, MarketplaceReadProjection.GetPlayerPurchases(db, cfg, "202").Count);
        Equal(1, MarketplaceReadProjection.GetPlayerListings(db, cfg, "101").Count);

        var sale = market.BuyNowForPlayer(listing.ListingId, 202, "BuyerOne", "web-buy-test-1");
        Equal(cfg.ToAtomic(15), sale.AmountAtomic);

        var sellerListings = MarketplaceReadProjection.GetPlayerListings(db, cfg, "101");
        Equal(1, sellerListings.Count);
        Equal("completed", sellerListings[0].Status);
        Equal("Moon Coin", sellerListings[0].AssetName);
        Equal(15m, sellerListings[0].Price);

        var purchases = MarketplaceReadProjection.GetPlayerPurchases(db, cfg, "202");
        Equal(1, purchases.Count);
        Equal(sale.SaleId, purchases[0].SaleId);
        Equal("Moon Coin", purchases[0].AssetName);
        Equal("SellerOne", purchases[0].SellerName);
        Equal("player", purchases[0].SellerType);
        Equal(15m, purchases[0].Amount);
        Equal("ARKOS", purchases[0].Currency);

        Equal(0, MarketplaceReadProjection.GetPlayerListings(db, cfg, "202").Count);
        Equal(0, MarketplaceReadProjection.GetPlayerPurchases(db, cfg, "999").Count);

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
