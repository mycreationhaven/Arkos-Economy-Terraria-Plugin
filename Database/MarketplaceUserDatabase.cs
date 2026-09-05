using ArkoviaEconomy.Models;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    public IReadOnlyList<ArkoviaAsset> GetAssetsForOwner(
        string ownerType,
        string ownerId,
        string? status = null,
        int limit = 50)
    {
        ownerType = ownerType.Trim().ToLowerInvariant();
        ownerId = ownerId.Trim();
        if (ownerType.Length == 0 || ownerId.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, 100);
        var result = new List<ArkoviaAsset>();
        using var r = string.IsNullOrWhiteSpace(status)
            ? _db.QueryReader(
                $"SELECT * FROM ArkoviaAssets WHERE OwnerType=@0 AND OwnerId=@1 ORDER BY UpdatedUtc DESC LIMIT {limit}",
                ownerType, ownerId)
            : _db.QueryReader(
                $"SELECT * FROM ArkoviaAssets WHERE OwnerType=@0 AND OwnerId=@1 AND Status=@2 ORDER BY UpdatedUtc DESC LIMIT {limit}",
                ownerType, ownerId, status.Trim().ToLowerInvariant());
        while (r.Read()) result.Add(ReadAsset(r));
        return result;
    }

    public IReadOnlyList<MarketplaceListing> GetMarketplaceListingsForOwner(
        string ownerType,
        string ownerId,
        int limit = 50)
    {
        ownerType = ownerType.Trim().ToLowerInvariant();
        ownerId = ownerId.Trim();
        if (ownerType.Length == 0 || ownerId.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, 100);
        var result = new List<MarketplaceListing>();
        using var r = _db.QueryReader(
            $"SELECT * FROM ArkoviaMarketplaceListings WHERE SellerOwnerType=@0 AND SellerOwnerId=@1 ORDER BY UpdatedUtc DESC LIMIT {limit}",
            ownerType, ownerId);
        while (r.Read()) result.Add(ReadMarketplaceListing(r));
        return result;
    }

    public IReadOnlyList<MarketplaceSale> GetMarketplaceSalesForBuyer(
        string ownerType,
        string ownerId,
        int limit = 50)
    {
        ownerType = ownerType.Trim().ToLowerInvariant();
        ownerId = ownerId.Trim();
        if (ownerType.Length == 0 || ownerId.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, 100);
        var result = new List<MarketplaceSale>();
        using var r = _db.QueryReader(
            $"SELECT * FROM ArkoviaMarketplaceSales WHERE BuyerOwnerType=@0 AND BuyerOwnerId=@1 ORDER BY CreatedUtc DESC LIMIT {limit}",
            ownerType, ownerId);
        while (r.Read()) result.Add(ReadMarketplaceSale(r));
        return result;
    }
}
