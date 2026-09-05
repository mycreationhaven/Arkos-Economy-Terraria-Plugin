using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    public ArkoviaProperty? GetPropertyByAsset(string assetId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaProperties WHERE AssetId=@0 AND Status='active' LIMIT 1", assetId);
        return r.Read() ? ReadProperty(r) : null;
    }

    public MarketplaceSale CompleteTownPropertySale(
        string listingId,
        string reservationKey,
        long escrowAccountId,
        int buyerUserId,
        string buyerAccountName,
        long salesTaxAtomic,
        long treasuryAccountId,
        string actor)
    {
        var prior = GetMarketplaceSaleByListing(listingId);
        if (prior is not null) return prior;

        var listing = GetMarketplaceListing(listingId)
            ?? throw new InvalidOperationException("Listing was not found.");
        if (!string.Equals(listing.Status, "reserved", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(listing.ReservationKey, reservationKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Listing is not reserved by that transaction.");
        if (!string.Equals(listing.SellerOwnerType, "town", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This settlement path only supports town-owned property.");
        if (listing.ReservedUntilUtc is DateTime expires && expires <= DateTime.UtcNow)
            throw new InvalidOperationException("Marketplace reservation expired before settlement.");

        var escrow = GetMarketplaceEscrow(reservationKey)
            ?? throw new InvalidOperationException("Escrow was not found.");
        if (!string.Equals(escrow.Status, "held", StringComparison.OrdinalIgnoreCase) ||
            escrow.AmountAtomic != listing.PriceAtomic)
            throw new InvalidOperationException("Escrow is not in a settleable state.");
        if (!string.Equals(escrow.BuyerOwnerType, "player", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(escrow.BuyerOwnerId, buyerUserId.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("Escrow buyer identity does not match the property buyer.");

        buyerAccountName = buyerAccountName.Trim();
        if (buyerAccountName.Length == 0)
            throw new InvalidOperationException("Buyer TShock account name is required for region ownership.");
        if (salesTaxAtomic < 0 || salesTaxAtomic > escrow.AmountAtomic)
            throw new InvalidOperationException("Marketplace sales tax is invalid.");

        var property = GetPropertyByAsset(listing.AssetId)
            ?? throw new InvalidOperationException("The listed asset is not an active Arkovia property.");
        if (!string.Equals(property.TownId, listing.SellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Property town ownership changed; settlement stopped.");

        var asset = GetAsset(listing.AssetId)
            ?? throw new InvalidOperationException("Listed asset was not found.");
        if (!string.Equals(asset.Status, "listed", StringComparison.OrdinalIgnoreCase) ||
            asset.Version != listing.AssetVersion ||
            !string.Equals(asset.OwnerType, "town", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, listing.SellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Asset ownership changed; settlement stopped.");

        var escrowAccount = GetAccountById(escrowAccountId)
            ?? throw new InvalidOperationException("Escrow account was not found.");
        var seller = GetAccountById(listing.SellerAccountId)
            ?? throw new InvalidOperationException("Seller settlement account was not found.");
        var treasury = GetAccountById(treasuryAccountId)
            ?? throw new InvalidOperationException("Marketplace tax treasury account was not found.");
        if (escrowAccount.WalletAtomic < escrow.AmountAtomic)
            throw new InvalidOperationException("Escrow account is underfunded.");
        if (seller.Id == treasury.Id && salesTaxAtomic > 0)
            throw new InvalidOperationException("Seller account and marketplace tax treasury cannot be the same account.");

        var sellerProceeds = escrow.AmountAtomic - salesTaxAtomic;
        var saleId = "ARK-SALE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var transferKey = "market-sale:" + listing.ListingId;
        var now = DateTime.UtcNow.ToString("O");

        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 " +
                "WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                escrowAccount.WalletAtomic - escrow.AmountAtomic, now, escrowAccount.Id,
                escrowAccount.WalletAtomic, escrowAccount.BankAtomic) != 1)
                throw new InvalidOperationException("Escrow balance changed; settlement was rolled back.");

            if (sellerProceeds > 0 && unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 " +
                "WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                checked(seller.WalletAtomic + sellerProceeds), now, seller.Id,
                seller.WalletAtomic, seller.BankAtomic) != 1)
                throw new InvalidOperationException("Seller balance changed; settlement was rolled back.");

            if (salesTaxAtomic > 0 && unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 " +
                "WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                checked(treasury.WalletAtomic + salesTaxAtomic), now, treasury.Id,
                treasury.WalletAtomic, treasury.BankAtomic) != 1)
                throw new InvalidOperationException("Treasury balance changed; settlement was rolled back.");

            if (unit.Execute(
                "UPDATE ArkoviaAssets SET OwnerType='player',OwnerId=@p0,Status='active',Version=Version+1,UpdatedUtc=@p1 " +
                "WHERE AssetId=@p2 AND OwnerType='town' AND OwnerId=@p3 AND Status='listed' AND Version=@p4",
                buyerUserId.ToString(), now, asset.AssetId, listing.SellerOwnerId, listing.AssetVersion) != 1)
                throw new InvalidOperationException("Property asset changed; settlement was rolled back.");

            if (unit.Execute(
                "UPDATE ArkoviaProperties SET TownId='',UpdatedUtc=@p0 " +
                "WHERE PropertyId=@p1 AND TownId=@p2 AND Status='active'",
                now, property.PropertyId, listing.SellerOwnerId) != 1)
                throw new InvalidOperationException("Property record changed; settlement was rolled back.");

            if (unit.Execute(
                "UPDATE Regions SET Owner=@p0,UserIds=@p1 WHERE RegionName=@p2 AND WorldID=@p3",
                buyerAccountName, buyerUserId.ToString(), property.RegionName, property.WorldKey) != 1)
                throw new InvalidOperationException("TShock region changed or is missing; settlement was rolled back.");

            unit.Execute(
                "INSERT INTO ArkoviaAssetTransfers " +
                "(TransferKey,AssetId,FromOwnerType,FromOwnerId,ToOwnerType,ToOwnerId,Reason,Actor,CreatedUtc) " +
                "VALUES (@p0,@p1,'town',@p2,'player',@p3,'marketplace_property_sale',@p4,@p5)",
                transferKey, asset.AssetId, listing.SellerOwnerId, buyerUserId.ToString(), actor, now);

            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceListings SET Status='completed',UpdatedUtc=@p0 " +
                "WHERE ListingId=@p1 AND Status='reserved' AND ReservationKey=@p2",
                now, listing.ListingId, reservationKey) != 1)
                throw new InvalidOperationException("Listing changed; settlement was rolled back.");

            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceEscrows SET Status='released',UpdatedUtc=@p0 " +
                "WHERE EscrowId=@p1 AND Status='held'",
                now, escrow.EscrowId) != 1)
                throw new InvalidOperationException("Escrow changed; settlement was rolled back.");

            unit.Execute(
                "INSERT INTO ArkoviaMarketplaceSales " +
                "(SaleId,ListingId,AssetId,SellerOwnerType,SellerOwnerId,BuyerOwnerType,BuyerOwnerId,AmountAtomic,TransferKey,CreatedUtc) " +
                "VALUES (@p0,@p1,@p2,'town',@p3,'player',@p4,@p5,@p6,@p7)",
                saleId, listing.ListingId, asset.AssetId, listing.SellerOwnerId,
                buyerUserId.ToString(), escrow.AmountAtomic, transferKey, now);

            if (sellerProceeds > 0)
                unit.Ledger("market-settle:" + listing.ListingId, escrowAccount.Id, seller.Id,
                    sellerProceeds, "marketplace_property_sale", listing.ListingId, actor);
            if (salesTaxAtomic > 0)
                unit.Ledger("market-tax:" + listing.ListingId, escrowAccount.Id, treasury.Id,
                    salesTaxAtomic, "marketplace_sales_tax", listing.ListingId, actor);
            return 0;
        });

        return GetMarketplaceSaleByListing(listingId)
            ?? throw new InvalidOperationException("Property marketplace sale did not persist.");
    }
}
