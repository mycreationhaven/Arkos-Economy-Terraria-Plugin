using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Terraria;
using TShockAPI;

namespace ArkoviaEconomy.Core;

public sealed class MarketplaceService
{
    private const string EscrowAccountName = "Marketplace Escrow";
    private readonly EconomyDatabase _db;
    private readonly EconomyService _economy;
    private readonly Func<EconomyConfig> _config;
    private readonly object _gate = new();

    public MarketplaceService(EconomyDatabase db, EconomyService economy, Func<EconomyConfig>? config = null)
    {
        _db = db;
        _economy = economy;
        _config = config ?? (() => new EconomyConfig());
    }

    public EconomyAccount GetEscrowAccount()
    {
        lock (_gate)
        {
            var existing = _db.GetSystemAccount(EscrowAccountName);
            if (existing is not null) return existing;
            var id = _db.CreateAccount(null, "system", EscrowAccountName, 0);
            return _db.GetAccountById(id)
                ?? throw new InvalidOperationException("Marketplace escrow account creation failed.");
        }
    }

    public MarketplaceListing ListPlayerAsset(int userId, string userName, string assetId, long priceAtomic)
    {
        var player = _economy.GetOrCreatePlayer(userId, userName);
        var asset = _db.GetAsset(assetId) ?? throw new InvalidOperationException("Asset was not found.");
        if (!string.Equals(asset.OwnerType, "player", StringComparison.OrdinalIgnoreCase) || asset.OwnerId != userId.ToString())
            throw new InvalidOperationException("You do not own that asset.");
        if (asset.AssetType is "town" or "land" or "property")
            throw new InvalidOperationException("Town and property assets require the property marketplace policy flow.");
        lock (_gate)
            return _db.CreateMarketplaceListing(asset.AssetId, "player", userId.ToString(), player.Id, priceAtomic);
    }

    public MarketplaceListing ListTownProperty(
        ArkoviaTown town,
        int actorUserId,
        string assetId,
        long priceAtomic,
        TownService towns)
    {
        towns.RequireMayor(town, actorUserId);
        var asset = _db.GetAsset(assetId) ?? throw new InvalidOperationException("Asset was not found.");
        if (!string.Equals(asset.OwnerType, "town", StringComparison.OrdinalIgnoreCase) || asset.OwnerId != town.TownId)
            throw new InvalidOperationException("That asset is not owned by this town.");
        _ = _db.GetTownProperties(town.TownId).FirstOrDefault(p => p.AssetId == assetId)
            ?? throw new InvalidOperationException("Only active region-backed town property can use this sale flow.");
        var treasury = towns.GetTreasuryAccount(town);
        lock (_gate)
            return _db.CreateMarketplaceListing(asset.AssetId, "town", town.TownId, treasury.Id, priceAtomic);
    }

    public MarketplaceEscrow ReserveForPlayer(
        string listingId,
        int buyerUserId,
        string buyerName,
        string operationKey,
        TimeSpan? lifetime = null)
    {
        var buyer = _economy.GetOrCreatePlayer(buyerUserId, buyerName);
        var escrow = GetEscrowAccount();
        lock (_gate)
            return _db.ReserveMarketplaceListing(
                listingId, "player", buyerUserId.ToString(), buyer.Id, escrow.Id,
                operationKey, lifetime ?? TimeSpan.FromMinutes(10), buyerName);
    }

    public MarketplaceSale Complete(string listingId, string reservationKey, string actor)
    {
        var escrow = GetEscrowAccount();
        lock (_gate)
            return _db.CompleteMarketplaceSale(listingId, reservationKey, escrow.Id, actor);
    }

    public MarketplaceSale BuyNowForPlayer(
        string listingId,
        int buyerUserId,
        string buyerName,
        string operationKey)
    {
        CleanupExpiredReservations();
        var listing = _db.GetMarketplaceListing(listingId)
            ?? throw new InvalidOperationException("Listing was not found.");
        var property = _db.GetPropertyByAsset(listing.AssetId);
        var held = ReserveForPlayer(listingId, buyerUserId, buyerName, operationKey);
        if (property is null)
            return Complete(listingId, held.ReservationKey, buyerName);

        if (!string.Equals(listing.SellerOwnerType, "town", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only town-to-player property sales are enabled in this marketplace version.");

        var cfg = _config();
        var salesTaxAtomic = checked((long)Math.Round(
            listing.PriceAtomic * cfg.MarketSalesTaxPercent / 100m,
            0,
            MidpointRounding.AwayFromZero));
        if (salesTaxAtomic < 0 || salesTaxAtomic > listing.PriceAtomic)
            throw new InvalidOperationException("Configured marketplace sales tax is invalid.");

        var escrowAccount = GetEscrowAccount();
        var treasury = _economy.GetTreasury();
        MarketplaceSale sale;
        lock (_gate)
        {
            sale = _db.CompleteTownPropertySale(
                listingId,
                held.ReservationKey,
                escrowAccount.Id,
                buyerUserId,
                buyerName,
                salesTaxAtomic,
                treasury.Id,
                buyerName);
        }

        if (property.WorldKey == Main.worldID.ToString())
            TShock.Regions.Reload();
        return sale;
    }

    public void CancelListing(string listingId, int actorUserId, TownService towns)
    {
        var listing = _db.GetMarketplaceListing(listingId)
            ?? throw new InvalidOperationException("Listing was not found.");
        if (string.Equals(listing.SellerOwnerType, "player", StringComparison.OrdinalIgnoreCase))
        {
            if (listing.SellerOwnerId != actorUserId.ToString())
                throw new InvalidOperationException("Only the seller can cancel this listing.");
        }
        else if (string.Equals(listing.SellerOwnerType, "town", StringComparison.OrdinalIgnoreCase))
        {
            var town = towns.RequireTownForUser(actorUserId);
            if (town.TownId != listing.SellerOwnerId)
                throw new InvalidOperationException("That listing belongs to another town.");
            towns.RequireMayor(town, actorUserId);
        }
        else
        {
            throw new InvalidOperationException("Unsupported marketplace seller type.");
        }

        lock (_gate)
            _db.CancelMarketplaceListing(listingId, listing.SellerOwnerType, listing.SellerOwnerId);
    }

    public void ReleaseReservation(string listingId, string reservationKey, string actor)
    {
        var escrow = GetEscrowAccount();
        lock (_gate)
            _db.ReleaseMarketplaceReservation(listingId, reservationKey, escrow.Id, actor);
    }

    public int CleanupExpiredReservations()
    {
        var now = DateTime.UtcNow;
        var expired = _db.GetMarketplaceListings("reserved", 100)
            .Where(x => x.ReservedUntilUtc is DateTime until && until <= now)
            .ToList();
        var released = 0;
        foreach (var listing in expired)
        {
            try
            {
                ReleaseReservation(listing.ListingId, listing.ReservationKey, "marketplace-expiry");
                released++;
            }
            catch (InvalidOperationException)
            {
                // Another caller may have settled or released it first.
            }
        }
        return released;
    }
}
