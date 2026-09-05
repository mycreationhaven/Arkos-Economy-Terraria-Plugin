using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Core;

public sealed class MarketplaceService
{
    private const string EscrowAccountName = "Marketplace Escrow";
    private readonly EconomyDatabase _db;
    private readonly EconomyService _economy;
    private readonly object _gate = new();

    public MarketplaceService(EconomyDatabase db, EconomyService economy)
    {
        _db = db;
        _economy = economy;
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
        var property = _db.GetTownProperties(town.TownId).FirstOrDefault(p => p.AssetId == assetId)
            ?? throw new InvalidOperationException("Only active region-backed town property can use this sale flow.");
        _ = property;
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
        var held = ReserveForPlayer(listingId, buyerUserId, buyerName, operationKey);
        return Complete(listingId, held.ReservationKey, buyerName);
    }

    public void ReleaseReservation(string listingId, string reservationKey, string actor)
    {
        var escrow = GetEscrowAccount();
        lock (_gate)
            _db.ReleaseMarketplaceReservation(listingId, reservationKey, escrow.Id, actor);
    }
}
