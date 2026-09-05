using MySql.Data.MySqlClient;
using ArkoviaEconomy.Models;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureMarketplaceSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaMarketplaceListings",
            new SqlColumn("ListingId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64),
            new SqlColumn("SellerOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("SellerOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("SellerAccountId", MySqlDbType.Int64),
            new SqlColumn("ListingType", MySqlDbType.VarChar, 32),
            new SqlColumn("PriceAtomic", MySqlDbType.Int64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("AssetVersion", MySqlDbType.Int32),
            new SqlColumn("ReservedByOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("ReservedByOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("ReservationKey", MySqlDbType.VarChar, 160),
            new SqlColumn("ReservedUntilUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaMarketplaceEscrows",
            new SqlColumn("EscrowId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("ListingId", MySqlDbType.VarChar, 64),
            new SqlColumn("ReservationKey", MySqlDbType.VarChar, 160) { Unique = true },
            new SqlColumn("BuyerOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("BuyerOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("BuyerAccountId", MySqlDbType.Int64),
            new SqlColumn("AmountAtomic", MySqlDbType.Int64),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaMarketplaceSales",
            new SqlColumn("SaleId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("ListingId", MySqlDbType.VarChar, 64) { Unique = true },
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64),
            new SqlColumn("SellerOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("SellerOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("BuyerOwnerType", MySqlDbType.VarChar, 32),
            new SqlColumn("BuyerOwnerId", MySqlDbType.VarChar, 64),
            new SqlColumn("AmountAtomic", MySqlDbType.Int64),
            new SqlColumn("TransferKey", MySqlDbType.VarChar, 160) { Unique = true },
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40)));
    }

    public MarketplaceListing? GetMarketplaceListing(string listingId)
    {
        using var r = _db.QueryReader("SELECT * FROM ArkoviaMarketplaceListings WHERE ListingId=@0 LIMIT 1", listingId);
        return r.Read() ? ReadMarketplaceListing(r) : null;
    }

    public IReadOnlyList<MarketplaceListing> GetMarketplaceListings(string status = "active", int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var result = new List<MarketplaceListing>();
        using var r = _db.QueryReader(
            $"SELECT * FROM ArkoviaMarketplaceListings WHERE Status=@0 ORDER BY CreatedUtc DESC LIMIT {limit}",
            status);
        while (r.Read()) result.Add(ReadMarketplaceListing(r));
        return result;
    }

    public MarketplaceEscrow? GetMarketplaceEscrow(string reservationKey)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaMarketplaceEscrows WHERE ReservationKey=@0 LIMIT 1", reservationKey);
        return r.Read() ? ReadMarketplaceEscrow(r) : null;
    }

    public MarketplaceSale? GetMarketplaceSaleByListing(string listingId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaMarketplaceSales WHERE ListingId=@0 LIMIT 1", listingId);
        return r.Read() ? ReadMarketplaceSale(r) : null;
    }

    public MarketplaceListing CreateMarketplaceListing(
        string assetId,
        string sellerOwnerType,
        string sellerOwnerId,
        long sellerAccountId,
        long priceAtomic,
        string listingType = "buy_now")
    {
        if (priceAtomic <= 0) throw new InvalidOperationException("Listing price must be positive.");
        sellerOwnerType = sellerOwnerType.Trim().ToLowerInvariant();
        sellerOwnerId = sellerOwnerId.Trim();
        if (sellerOwnerType.Length == 0 || sellerOwnerId.Length == 0)
            throw new InvalidOperationException("Seller ownership identity is required.");

        var asset = GetAsset(assetId) ?? throw new InvalidOperationException("Asset was not found.");
        if (!string.Equals(asset.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only active assets can be listed.");
        if (!string.Equals(asset.OwnerType, sellerOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, sellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Seller does not own that asset.");
        _ = GetAccountById(sellerAccountId) ?? throw new InvalidOperationException("Seller settlement account was not found.");

        var listingId = "ARK-LIST-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var now = DateTime.UtcNow.ToString("O");
        var listedVersion = checked(asset.Version + 1);
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaAssets SET Status='listed',Version=@p0,UpdatedUtc=@p1 " +
                "WHERE AssetId=@p2 AND OwnerType=@p3 AND OwnerId=@p4 AND Status='active' AND Version=@p5",
                listedVersion, now, asset.AssetId, asset.OwnerType, asset.OwnerId, asset.Version) != 1)
                throw new InvalidOperationException("Asset changed during listing; retry.");

            unit.Execute(
                "INSERT INTO ArkoviaMarketplaceListings " +
                "(ListingId,AssetId,SellerOwnerType,SellerOwnerId,SellerAccountId,ListingType,PriceAtomic,Status,AssetVersion," +
                "ReservedByOwnerType,ReservedByOwnerId,ReservationKey,ReservedUntilUtc,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,'active',@p7,'','','','',@p8,@p8)",
                listingId, asset.AssetId, sellerOwnerType, sellerOwnerId, sellerAccountId,
                listingType.Trim().ToLowerInvariant(), priceAtomic, listedVersion, now);
            return 0;
        });
        return GetMarketplaceListing(listingId)
            ?? throw new InvalidOperationException("Marketplace listing did not persist.");
    }

    public void CancelMarketplaceListing(string listingId, string sellerOwnerType, string sellerOwnerId)
    {
        var listing = GetMarketplaceListing(listingId) ?? throw new InvalidOperationException("Listing was not found.");
        if (!string.Equals(listing.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only an active, unreserved listing can be cancelled.");
        if (!string.Equals(listing.SellerOwnerType, sellerOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(listing.SellerOwnerId, sellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the seller can cancel this listing.");
        var asset = GetAsset(listing.AssetId) ?? throw new InvalidOperationException("Listed asset was not found.");
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceListings SET Status='cancelled',UpdatedUtc=@p0 " +
                "WHERE ListingId=@p1 AND Status='active'",
                now, listing.ListingId) != 1)
                throw new InvalidOperationException("Listing changed; retry.");
            if (unit.Execute(
                "UPDATE ArkoviaAssets SET Status='active',Version=Version+1,UpdatedUtc=@p0 " +
                "WHERE AssetId=@p1 AND Status='listed' AND Version=@p2 AND OwnerType=@p3 AND OwnerId=@p4",
                now, asset.AssetId, listing.AssetVersion, listing.SellerOwnerType, listing.SellerOwnerId) != 1)
                throw new InvalidOperationException("Listed asset changed; cancellation was rolled back.");
            return 0;
        });
    }

    public MarketplaceEscrow ReserveMarketplaceListing(
        string listingId,
        string buyerOwnerType,
        string buyerOwnerId,
        long buyerAccountId,
        long escrowAccountId,
        string reservationKey,
        TimeSpan lifetime,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(reservationKey)) throw new InvalidOperationException("Reservation key is required.");
        var existing = GetMarketplaceEscrow(reservationKey);
        if (existing is not null) return existing;

        var listing = GetMarketplaceListing(listingId) ?? throw new InvalidOperationException("Listing was not found.");
        if (!string.Equals(listing.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Listing is not available.");
        buyerOwnerType = buyerOwnerType.Trim().ToLowerInvariant();
        buyerOwnerId = buyerOwnerId.Trim();
        if (buyerOwnerType == listing.SellerOwnerType && buyerOwnerId == listing.SellerOwnerId)
            throw new InvalidOperationException("You cannot buy your own listing.");
        var asset = GetAsset(listing.AssetId) ?? throw new InvalidOperationException("Listed asset was not found.");
        if (!string.Equals(asset.Status, "listed", StringComparison.OrdinalIgnoreCase) || asset.Version != listing.AssetVersion ||
            !string.Equals(asset.OwnerType, listing.SellerOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, listing.SellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Listed asset changed; listing requires review.");

        var buyer = GetAccountById(buyerAccountId) ?? throw new InvalidOperationException("Buyer account was not found.");
        var escrowAccount = GetAccountById(escrowAccountId) ?? throw new InvalidOperationException("Escrow account was not found.");
        if (buyer.Frozen) throw new InvalidOperationException("Buyer account is frozen.");
        if (buyer.WalletAtomic < listing.PriceAtomic) throw new InvalidOperationException("Insufficient wallet balance.");

        var escrowId = "ARK-ESCROW-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var nowUtc = DateTime.UtcNow;
        var now = nowUtc.ToString("O");
        var expires = nowUtc.Add(lifetime);
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 " +
                "WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4 AND Frozen=0",
                buyer.WalletAtomic - listing.PriceAtomic, now, buyer.Id, buyer.WalletAtomic, buyer.BankAtomic) != 1)
                throw new InvalidOperationException("Buyer balance changed; no funds were reserved. Retry.");
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 " +
                "WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                checked(escrowAccount.WalletAtomic + listing.PriceAtomic), now, escrowAccount.Id,
                escrowAccount.WalletAtomic, escrowAccount.BankAtomic) != 1)
                throw new InvalidOperationException("Escrow balance changed; no funds were reserved. Retry.");
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceListings SET Status='reserved',ReservedByOwnerType=@p0,ReservedByOwnerId=@p1," +
                "ReservationKey=@p2,ReservedUntilUtc=@p3,UpdatedUtc=@p4 WHERE ListingId=@p5 AND Status='active'",
                buyerOwnerType, buyerOwnerId, reservationKey, expires.ToString("O"), now, listing.ListingId) != 1)
                throw new InvalidOperationException("Listing was reserved by someone else; no funds were moved.");
            unit.Execute(
                "INSERT INTO ArkoviaMarketplaceEscrows " +
                "(EscrowId,ListingId,ReservationKey,BuyerOwnerType,BuyerOwnerId,BuyerAccountId,AmountAtomic,Status,CreatedUtc,UpdatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,'held',@p7,@p7)",
                escrowId, listing.ListingId, reservationKey, buyerOwnerType, buyerOwnerId,
                buyerAccountId, listing.PriceAtomic, now);
            unit.Ledger("market-reserve:" + reservationKey, buyer.Id, escrowAccount.Id, listing.PriceAtomic,
                "marketplace_escrow", listing.ListingId, actor);
            return 0;
        });
        return GetMarketplaceEscrow(reservationKey)
            ?? throw new InvalidOperationException("Marketplace escrow did not persist.");
    }

    public MarketplaceSale CompleteMarketplaceSale(
        string listingId,
        string reservationKey,
        long escrowAccountId,
        string actor)
    {
        var prior = GetMarketplaceSaleByListing(listingId);
        if (prior is not null) return prior;

        var listing = GetMarketplaceListing(listingId) ?? throw new InvalidOperationException("Listing was not found.");
        if (!string.Equals(listing.Status, "reserved", StringComparison.OrdinalIgnoreCase) || listing.ReservationKey != reservationKey)
            throw new InvalidOperationException("Listing is not reserved by that transaction.");
        if (listing.ReservedUntilUtc is DateTime expires && expires <= DateTime.UtcNow)
            throw new InvalidOperationException("Marketplace reservation expired before settlement.");
        var escrow = GetMarketplaceEscrow(reservationKey) ?? throw new InvalidOperationException("Escrow was not found.");
        if (!string.Equals(escrow.Status, "held", StringComparison.OrdinalIgnoreCase) || escrow.AmountAtomic != listing.PriceAtomic)
            throw new InvalidOperationException("Escrow is not in a settleable state.");
        var asset = GetAsset(listing.AssetId) ?? throw new InvalidOperationException("Listed asset was not found.");
        if (!string.Equals(asset.Status, "listed", StringComparison.OrdinalIgnoreCase) || asset.Version != listing.AssetVersion ||
            !string.Equals(asset.OwnerType, listing.SellerOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, listing.SellerOwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Asset ownership changed; settlement stopped.");
        var escrowAccount = GetAccountById(escrowAccountId) ?? throw new InvalidOperationException("Escrow account was not found.");
        var seller = GetAccountById(listing.SellerAccountId) ?? throw new InvalidOperationException("Seller account was not found.");
        if (escrowAccount.WalletAtomic < escrow.AmountAtomic)
            throw new InvalidOperationException("Escrow account is underfunded.");

        var saleId = "ARK-SALE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var transferKey = "market-sale:" + listing.ListingId;
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                escrowAccount.WalletAtomic - escrow.AmountAtomic, now, escrowAccount.Id,
                escrowAccount.WalletAtomic, escrowAccount.BankAtomic) != 1)
                throw new InvalidOperationException("Escrow balance changed; settlement was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                checked(seller.WalletAtomic + escrow.AmountAtomic), now, seller.Id, seller.WalletAtomic, seller.BankAtomic) != 1)
                throw new InvalidOperationException("Seller balance changed; settlement was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaAssets SET OwnerType=@p0,OwnerId=@p1,Status='active',Version=Version+1,UpdatedUtc=@p2 " +
                "WHERE AssetId=@p3 AND OwnerType=@p4 AND OwnerId=@p5 AND Status='listed' AND Version=@p6",
                escrow.BuyerOwnerType, escrow.BuyerOwnerId, now, asset.AssetId,
                listing.SellerOwnerType, listing.SellerOwnerId, listing.AssetVersion) != 1)
                throw new InvalidOperationException("Asset changed; settlement was rolled back.");
            unit.Execute(
                "INSERT INTO ArkoviaAssetTransfers " +
                "(TransferKey,AssetId,FromOwnerType,FromOwnerId,ToOwnerType,ToOwnerId,Reason,Actor,CreatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,'marketplace_sale',@p6,@p7)",
                transferKey, asset.AssetId, listing.SellerOwnerType, listing.SellerOwnerId,
                escrow.BuyerOwnerType, escrow.BuyerOwnerId, actor, now);
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceListings SET Status='completed',UpdatedUtc=@p0 WHERE ListingId=@p1 AND Status='reserved' AND ReservationKey=@p2",
                now, listing.ListingId, reservationKey) != 1)
                throw new InvalidOperationException("Listing changed; settlement was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceEscrows SET Status='released',UpdatedUtc=@p0 WHERE EscrowId=@p1 AND Status='held'",
                now, escrow.EscrowId) != 1)
                throw new InvalidOperationException("Escrow changed; settlement was rolled back.");
            unit.Execute(
                "INSERT INTO ArkoviaMarketplaceSales " +
                "(SaleId,ListingId,AssetId,SellerOwnerType,SellerOwnerId,BuyerOwnerType,BuyerOwnerId,AmountAtomic,TransferKey,CreatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                saleId, listing.ListingId, asset.AssetId, listing.SellerOwnerType, listing.SellerOwnerId,
                escrow.BuyerOwnerType, escrow.BuyerOwnerId, escrow.AmountAtomic, transferKey, now);
            unit.Ledger("market-settle:" + listing.ListingId, escrowAccount.Id, seller.Id, escrow.AmountAtomic,
                "marketplace_sale", listing.ListingId, actor);
            return 0;
        });
        return GetMarketplaceSaleByListing(listingId)
            ?? throw new InvalidOperationException("Marketplace sale did not persist.");
    }

    public void ReleaseMarketplaceReservation(
        string listingId,
        string reservationKey,
        long escrowAccountId,
        string actor)
    {
        var listing = GetMarketplaceListing(listingId) ?? throw new InvalidOperationException("Listing was not found.");
        if (!string.Equals(listing.Status, "reserved", StringComparison.OrdinalIgnoreCase) || listing.ReservationKey != reservationKey)
            throw new InvalidOperationException("Listing reservation does not match.");
        var escrow = GetMarketplaceEscrow(reservationKey) ?? throw new InvalidOperationException("Escrow was not found.");
        if (!string.Equals(escrow.Status, "held", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escrow is not held.");
        var escrowAccount = GetAccountById(escrowAccountId) ?? throw new InvalidOperationException("Escrow account was not found.");
        var buyer = GetAccountById(escrow.BuyerAccountId) ?? throw new InvalidOperationException("Buyer account was not found.");
        if (escrowAccount.WalletAtomic < escrow.AmountAtomic)
            throw new InvalidOperationException("Escrow account is underfunded.");
        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                escrowAccount.WalletAtomic - escrow.AmountAtomic, now, escrowAccount.Id,
                escrowAccount.WalletAtomic, escrowAccount.BankAtomic) != 1)
                throw new InvalidOperationException("Escrow balance changed; refund was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 WHERE Id=@p2 AND WalletAtomic=@p3 AND BankAtomic=@p4",
                checked(buyer.WalletAtomic + escrow.AmountAtomic), now, buyer.Id, buyer.WalletAtomic, buyer.BankAtomic) != 1)
                throw new InvalidOperationException("Buyer balance changed; refund was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceListings SET Status='active',ReservedByOwnerType='',ReservedByOwnerId=''," +
                "ReservationKey='',ReservedUntilUtc='',UpdatedUtc=@p0 WHERE ListingId=@p1 AND Status='reserved' AND ReservationKey=@p2",
                now, listing.ListingId, reservationKey) != 1)
                throw new InvalidOperationException("Listing changed; refund was rolled back.");
            if (unit.Execute(
                "UPDATE ArkoviaMarketplaceEscrows SET Status='refunded',UpdatedUtc=@p0 WHERE EscrowId=@p1 AND Status='held'",
                now, escrow.EscrowId) != 1)
                throw new InvalidOperationException("Escrow changed; refund was rolled back.");
            unit.Ledger("market-refund:" + reservationKey, escrowAccount.Id, buyer.Id, escrow.AmountAtomic,
                "marketplace_refund", listing.ListingId, actor);
            return 0;
        });
    }

    private static MarketplaceListing ReadMarketplaceListing(QueryResult r)
    {
        var reservedText = r.Get<string>("ReservedUntilUtc");
        return new MarketplaceListing(
            r.Get<string>("ListingId"), r.Get<string>("AssetId"), r.Get<string>("SellerOwnerType"),
            r.Get<string>("SellerOwnerId"), r.Get<long>("SellerAccountId"), r.Get<string>("ListingType"),
            r.Get<long>("PriceAtomic"), r.Get<string>("Status"), r.Get<int>("AssetVersion"),
            r.Get<string>("ReservedByOwnerType"), r.Get<string>("ReservedByOwnerId"), r.Get<string>("ReservationKey"),
            string.IsNullOrWhiteSpace(reservedText) ? null : DateTime.Parse(reservedText),
            DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc")));
    }

    private static MarketplaceEscrow ReadMarketplaceEscrow(QueryResult r) => new(
        r.Get<string>("EscrowId"), r.Get<string>("ListingId"), r.Get<string>("ReservationKey"),
        r.Get<string>("BuyerOwnerType"), r.Get<string>("BuyerOwnerId"), r.Get<long>("BuyerAccountId"),
        r.Get<long>("AmountAtomic"), r.Get<string>("Status"), DateTime.Parse(r.Get<string>("CreatedUtc")),
        DateTime.Parse(r.Get<string>("UpdatedUtc")));

    private static MarketplaceSale ReadMarketplaceSale(QueryResult r) => new(
        r.Get<string>("SaleId"), r.Get<string>("ListingId"), r.Get<string>("AssetId"),
        r.Get<string>("SellerOwnerType"), r.Get<string>("SellerOwnerId"),
        r.Get<string>("BuyerOwnerType"), r.Get<string>("BuyerOwnerId"), r.Get<long>("AmountAtomic"),
        r.Get<string>("TransferKey"), DateTime.Parse(r.Get<string>("CreatedUtc")));
}
