using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Rests;
using TShockAPI;

namespace ArkoviaEconomy.Api;

/// <summary>
/// Read-only marketplace API intended for a trusted web backend. The browser should never
/// receive a TShock REST token or connect directly to the economy database.
/// </summary>
public sealed class MarketplaceReadApi(EconomyDatabase db, Func<EconomyConfig> config) : IDisposable
{
    private volatile bool _active = true;
    private bool _registered;

    public void Register()
    {
        if (_registered || !config().Api.EnablePublicReadApi)
            return;

        TShock.RestApi.Register(new SecureRestCommand(
            "Arkovia marketplace status",
            "/marketplace/api/v1/status",
            Status,
            Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand(
            "Arkovia marketplace listings",
            "/marketplace/api/v1/listings",
            Listings,
            Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand(
            "Arkovia marketplace listing",
            "/marketplace/api/v1/listings/{listingId}",
            Listing,
            Permissions.MarketplaceApiRead));

        _registered = true;
        if (!TShock.Config.Settings.RestApiEnabled)
            Core.EconomyLog.Warn("[ArkoviaEconomy] Marketplace read API routes registered, but TShock RestApiEnabled is false.");
    }

    private object Status(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var cfg = config();
        var result = new RestObject();
        result["api"] = "arkovia-marketplace";
        result["version"] = 1;
        result["currency"] = cfg.CurrencySymbol;
        result["decimals"] = cfg.Decimals;
        result["readOnly"] = true;
        return result;
    }

    private object Listings(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var limit = ParseLimit(args.Parameters["limit"]);
        var views = MarketplaceReadProjection.GetActiveListings(db, config(), limit);
        var result = new RestObject();
        result["count"] = views.Count;
        result["listings"] = views;
        return result;
    }

    private object Listing(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var listingId = (args.Parameters["listingId"] ?? string.Empty).Trim();
        if (listingId.Length is < 10 or > 64 || !listingId.StartsWith("ARK-LIST-", StringComparison.Ordinal))
            return new RestObject("400") { Error = "Invalid listing ID." };

        var view = MarketplaceReadProjection.GetActiveListing(db, config(), listingId);
        if (view is null)
            return new RestObject("404") { Error = "Listing was not found or is no longer available." };

        var result = new RestObject();
        result["listing"] = view;
        return result;
    }

    private static int ParseLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 50;
        return int.TryParse(value, out var limit) ? Math.Clamp(limit, 1, 100) : 50;
    }

    private static RestObject Disabled() =>
        new("503") { Error = "Marketplace API is unavailable until the plugin is restarted." };

    public void Dispose() => _active = false;
}

public sealed record MarketplaceListingView(
    string ListingId,
    string AssetId,
    string AssetType,
    string AssetName,
    string ListingType,
    long PriceAtomic,
    decimal Price,
    string Currency,
    string SellerType,
    string SellerName,
    string? PropertyType,
    string? RegionName,
    DateTime CreatedUtc);

public static class MarketplaceReadProjection
{
    public static IReadOnlyList<MarketplaceListingView> GetActiveListings(
        EconomyDatabase db,
        EconomyConfig config,
        int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var result = new List<MarketplaceListingView>();
        foreach (var listing in db.GetMarketplaceListings("active", limit))
        {
            var view = ProjectActiveListing(db, config, listing);
            if (view is not null) result.Add(view);
        }
        return result;
    }

    public static MarketplaceListingView? GetActiveListing(
        EconomyDatabase db,
        EconomyConfig config,
        string listingId)
    {
        var listing = db.GetMarketplaceListing(listingId);
        if (listing is null || !string.Equals(listing.Status, "active", StringComparison.OrdinalIgnoreCase))
            return null;
        return ProjectActiveListing(db, config, listing);
    }

    private static MarketplaceListingView? ProjectActiveListing(
        EconomyDatabase db,
        EconomyConfig config,
        MarketplaceListing listing)
    {
        var asset = db.GetAsset(listing.AssetId);
        if (asset is null || !string.Equals(asset.Status, "listed", StringComparison.OrdinalIgnoreCase) ||
            asset.Version != listing.AssetVersion ||
            !string.Equals(asset.OwnerType, listing.SellerOwnerType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, listing.SellerOwnerId, StringComparison.Ordinal))
            return null;

        string sellerName;
        if (string.Equals(listing.SellerOwnerType, "town", StringComparison.OrdinalIgnoreCase))
            sellerName = db.GetTown(listing.SellerOwnerId)?.Name ?? "Town";
        else
            sellerName = db.GetAccountById(listing.SellerAccountId)?.Name ?? "Player";

        var property = db.GetPropertyByAsset(listing.AssetId);
        return new MarketplaceListingView(
            listing.ListingId,
            listing.AssetId,
            asset.AssetType,
            asset.Name,
            listing.ListingType,
            listing.PriceAtomic,
            config.FromAtomic(listing.PriceAtomic),
            config.CurrencySymbol,
            listing.SellerOwnerType,
            sellerName,
            property?.PropertyType,
            property?.RegionName,
            listing.CreatedUtc);
    }
}
