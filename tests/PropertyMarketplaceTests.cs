using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class PropertyMarketplaceTests
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

        var path = Path.Combine(Path.GetTempPath(), $"arkovia-property-market-{Guid.NewGuid():N}.sqlite");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var db = new EconomyDatabase(connection);
        db.EnsureSchema();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE Regions (Id INTEGER PRIMARY KEY AUTOINCREMENT, RegionName TEXT NOT NULL, WorldID TEXT NOT NULL, Owner TEXT NOT NULL, UserIds TEXT NOT NULL)";
            command.ExecuteNonQuery();
        }

        var town = db.CreateTownBundle("Market Town", 101);
        var property = db.CreateTownProperty(town.TownId, "land", "777", "Market Estate", "Market Estate");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Regions (RegionName,WorldID,Owner,UserIds) VALUES ('Market Estate','777','Mayor','101')";
            command.ExecuteNonQuery();
        }

        var buyerId = db.CreateAccount(202, "player", "Property Buyer", 2_000);
        var escrowId = db.CreateAccount(null, "system", "Property Marketplace Escrow", 0);
        var taxTreasuryId = db.CreateAccount(null, "system", "Property Tax Treasury", 50);
        var sellerBefore = db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic;
        var taxBefore = db.GetAccountById(taxTreasuryId)!.WalletAtomic;

        var listing = db.CreateMarketplaceListing(
            property.AssetId, "town", town.TownId, town.TreasuryAccountId, 1_000);
        var held = db.ReserveMarketplaceListing(
            listing.ListingId, "player", "202", buyerId, escrowId,
            "property-market-reserve-1", TimeSpan.FromMinutes(10), "Property Buyer");

        var sale = db.CompleteTownPropertySale(
            listing.ListingId, held.ReservationKey, escrowId,
            202, "PropertyBuyer", 100, taxTreasuryId, "Property Buyer");

        Equal(1_000L, sale.AmountAtomic);
        Equal(1_000L, db.GetAccountById(buyerId)!.WalletAtomic);
        Equal(0L, db.GetAccountById(escrowId)!.WalletAtomic);
        Equal(sellerBefore + 900, db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic);
        Equal(taxBefore + 100, db.GetAccountById(taxTreasuryId)!.WalletAtomic);
        Equal("player", db.GetAsset(property.AssetId)!.OwnerType);
        Equal("202", db.GetAsset(property.AssetId)!.OwnerId);
        Equal("active", db.GetAsset(property.AssetId)!.Status);
        Equal(null, db.GetPropertyByAsset(property.AssetId)!.TownId);
        Equal("completed", db.GetMarketplaceListing(listing.ListingId)!.Status);
        Equal("released", db.GetMarketplaceEscrow(held.ReservationKey)!.Status);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Owner || ':' || UserIds FROM Regions WHERE RegionName='Market Estate' AND WorldID='777'";
            Equal("PropertyBuyer:202", Convert.ToString(command.ExecuteScalar()));
        }

        // Replaying a completed property sale is idempotent.
        var replay = db.CompleteTownPropertySale(
            listing.ListingId, held.ReservationKey, escrowId,
            202, "PropertyBuyer", 100, taxTreasuryId, "Property Buyer");
        Equal(sale.SaleId, replay.SaleId);
        Equal(sellerBefore + 900, db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic);
        Equal(taxBefore + 100, db.GetAccountById(taxTreasuryId)!.WalletAtomic);

        // A transfer-audit failure rolls back money, asset, property and TShock region changes together.
        var rollbackProperty = db.CreateTownProperty(town.TownId, "land", "777", "Rollback Estate", "Rollback Estate");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Regions (RegionName,WorldID,Owner,UserIds) VALUES ('Rollback Estate','777','Mayor','101')";
            command.ExecuteNonQuery();
        }
        var rollbackListing = db.CreateMarketplaceListing(
            rollbackProperty.AssetId, "town", town.TownId, town.TreasuryAccountId, 300);
        var rollbackHeld = db.ReserveMarketplaceListing(
            rollbackListing.ListingId, "player", "202", buyerId, escrowId,
            "property-market-rollback-1", TimeSpan.FromMinutes(10), "Property Buyer");
        var rollbackSellerBefore = db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic;
        var rollbackTaxBefore = db.GetAccountById(taxTreasuryId)!.WalletAtomic;
        var rollbackEscrowBefore = db.GetAccountById(escrowId)!.WalletAtomic;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_property_transfer BEFORE INSERT ON ArkoviaAssetTransfers BEGIN SELECT RAISE(ABORT, 'simulated property transfer failure'); END";
            command.ExecuteNonQuery();
        }
        try
        {
            db.CompleteTownPropertySale(
                rollbackListing.ListingId, rollbackHeld.ReservationKey, escrowId,
                202, "PropertyBuyer", 30, taxTreasuryId, "tests");
            throw new Exception("Expected property marketplace settlement rollback.");
        }
        catch (SqliteException)
        {
            checks++;
        }

        Equal(rollbackSellerBefore, db.GetAccountById(town.TreasuryAccountId)!.WalletAtomic);
        Equal(rollbackTaxBefore, db.GetAccountById(taxTreasuryId)!.WalletAtomic);
        Equal(rollbackEscrowBefore, db.GetAccountById(escrowId)!.WalletAtomic);
        Equal("town", db.GetAsset(rollbackProperty.AssetId)!.OwnerType);
        Equal(town.TownId, db.GetAsset(rollbackProperty.AssetId)!.OwnerId);
        Equal(town.TownId, db.GetPropertyByAsset(rollbackProperty.AssetId)!.TownId);
        Equal("reserved", db.GetMarketplaceListing(rollbackListing.ListingId)!.Status);
        Equal("held", db.GetMarketplaceEscrow(rollbackHeld.ReservationKey)!.Status);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Owner || ':' || UserIds FROM Regions WHERE RegionName='Rollback Estate' AND WorldID='777'";
            Equal("Mayor:101", Convert.ToString(command.ExecuteScalar()));
        }

        connection.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return checks;
    }
}
