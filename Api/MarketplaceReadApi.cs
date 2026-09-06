using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Rests;
using TShockAPI;

namespace ArkoviaEconomy.Api;

/// <summary>
/// Marketplace API intended for a trusted web backend. The browser should never
/// receive a TShock REST token or connect directly to the economy database.
/// </summary>
public sealed class MarketplaceReadApi(
    EconomyDatabase db,
    Func<EconomyConfig> config,
    MarketplaceAccountLinkService links,
    PlayerTradingService? trading = null) : IDisposable
{
    private volatile bool _active = true;
    private bool _registered;

    public void Register()
    {
        if (_registered || !config().Api.EnablePublicReadApi)
            return;

        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace status", "/marketplace/api/v1/status", Status, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace listings", "/marketplace/api/v1/listings", Listings, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace listing", "/marketplace/api/v1/listings/{listingId}", Listing, Permissions.MarketplaceApiRead));

        var meCommand = new SecureRestCommand("Arkovia marketplace linked account", "/marketplace/api/v1/me/{subject}", Me, Permissions.MarketplaceApiRead) { DoLog = false };
        TShock.RestApi.Register(meCommand);
        var linkCommand = new SecureRestCommand("Arkovia marketplace account link", "/marketplace/api/v1/link/{account}/{code}/{subject}", LinkAccount, Permissions.MarketplaceApiLink) { DoLog = false };
        TShock.RestApi.Register(linkCommand);

        _registered = true;
        if (!TShock.Config.Settings.RestApiEnabled)
            EconomyLog.Warn("[ArkoviaEconomy] Marketplace API routes registered, but TShock RestApiEnabled is false.");
    }

    private object Status(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var cfg = config();
        var result = new RestObject();
        result["api"] = "arkovia-marketplace"; result["version"] = 1; result["currency"] = cfg.CurrencySymbol; result["decimals"] = cfg.Decimals; result["readOnly"] = true;
        return result;
    }

    private object Listings(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var limit = ParseLimit(args.Parameters["limit"]);
        var views = MarketplaceReadProjection.GetActiveListings(db, config(), limit);
        var result = new RestObject(); result["count"] = views.Count; result["listings"] = views; return result;
    }

    private object Listing(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var listingId = (args.Verbs["listingId"] ?? string.Empty).Trim();
        if (listingId.Length is < 10 or > 64 || !listingId.StartsWith("ARK-LIST-", StringComparison.Ordinal))
            return new RestObject("400") { Error = "Invalid listing ID." };
        var view = MarketplaceReadProjection.GetActiveListing(db, config(), listingId);
        if (view is null) return new RestObject("404") { Error = "Listing was not found or is no longer available." };
        var result = new RestObject(); result["listing"] = view; return result;
    }

    private object Me(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var subject = (args.Verbs["subject"] ?? string.Empty).Trim();
        var link = db.GetWebAccountLinkBySubject(subject);
        if (link is null) return new RestObject("404") { Error = "No linked Terraria account was found." };
        var cfg = config(); var ownerId = link.TShockUserId.ToString(); var result = new RestObject();
        result["linked"] = true; result["accountName"] = link.TShockAccountName; result["linkedUtc"] = link.LinkedUtc;
        result["sellableAssets"] = MarketplaceReadProjection.GetPlayerSellableAssets(db, ownerId, 100);
        result["listings"] = MarketplaceReadProjection.GetPlayerListings(db, cfg, ownerId, 100);
        result["purchases"] = MarketplaceReadProjection.GetPlayerPurchases(db, cfg, ownerId, 100);
        result["stocks"] = trading?.Holdings(link.TShockUserId).Select(x => new { x.Ticker, x.Name, x.Shares, x.PriceAtomic, price = cfg.FromAtomic(x.PriceAtomic), x.MarketValueAtomic, marketValue = cfg.FromAtomic(x.MarketValueAtomic) }).ToList() ?? [];
        return result;
    }

    private object LinkAccount(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        try
        {
            var account = args.Verbs["account"] ?? string.Empty;
            var code = args.Verbs["code"] ?? string.Empty;
            var subject = args.Verbs["subject"] ?? string.Empty;
            var link = links.Redeem(account, code, subject);
            var result = new RestObject();
            result["linkId"] = link.LinkId;
            result["tshockUserId"] = link.TShockUserId;
            result["accountName"] = link.TShockAccountName;
            result["webSubject"] = link.WebSubject;
            result["linkedUtc"] = link.LinkedUtc;
            return result;
        }
        catch (InvalidOperationException ex) { return new RestObject("400") { Error = ex.Message }; }
    }

    private static int ParseLimit(string? value) { if (string.IsNullOrWhiteSpace(value)) return 50; return int.TryParse(value, out var limit) ? Math.Clamp(limit, 1, 100) : 50; }
    private static RestObject Disabled() => new("503") { Error = "Marketplace API is unavailable until the plugin is restarted." };
    public void Dispose() => _active = false;
}

public sealed record MarketplaceListingView(string ListingId,string AssetId,string AssetType,string AssetName,string ListingType,long PriceAtomic,decimal Price,string Currency,string SellerType,string SellerName,string? PropertyType,string? RegionName,DateTime CreatedUtc);
public sealed record MarketplaceSellableAssetView(string AssetId,string AssetType,string Name,string Status,int Version,DateTime CreatedUtc);
public sealed record MarketplacePurchaseView(string SaleId,string ListingId,string AssetId,string AssetType,string AssetName,decimal Amount,string Currency,string SellerType,string SellerName,DateTime SoldUtc);

public static class MarketplaceReadProjection
{
    public static IReadOnlyList<MarketplaceListingView> GetActiveListings(EconomyDatabase db, EconomyConfig cfg, int limit) => db.GetMarketplaceListings("active", limit).Select(x => ProjectListing(db,cfg,x)).ToList();
    public static MarketplaceListingView? GetActiveListing(EconomyDatabase db, EconomyConfig cfg, string listingId) { var l=db.GetMarketplaceListing(listingId); return l is not null && l.Status=="active" ? ProjectListing(db,cfg,l) : null; }
    public static IReadOnlyList<MarketplaceSellableAssetView> GetPlayerSellableAssets(EconomyDatabase db,string ownerId,int limit) => db.GetAssetsByOwner("player",ownerId,limit).Where(x=>x.Status=="active").Select(x=>new MarketplaceSellableAssetView(x.AssetId,x.AssetType,x.Name,x.Status,x.Version,x.CreatedUtc)).ToList();
    public static IReadOnlyList<MarketplaceListingView> GetPlayerListings(EconomyDatabase db,EconomyConfig cfg,string ownerId,int limit) => db.GetMarketplaceListingsForSeller("player",ownerId,limit).Select(x=>ProjectListing(db,cfg,x)).ToList();
    public static IReadOnlyList<MarketplacePurchaseView> GetPlayerPurchases(EconomyDatabase db,EconomyConfig cfg,string ownerId,int limit) => db.GetMarketplaceSalesForBuyer("player",ownerId,limit).Select(x=>{var asset=db.GetAsset(x.AssetId);return new MarketplacePurchaseView(x.SaleId,x.ListingId,x.AssetId,asset?.AssetType??"unknown",asset?.Name??x.AssetId,cfg.FromAtomic(x.GrossAtomic),cfg.CurrencySymbol,x.SellerType,DisplayOwner(db,x.SellerType,x.SellerId),x.SoldUtc);}).ToList();
    private static MarketplaceListingView ProjectListing(EconomyDatabase db,EconomyConfig cfg,MarketplaceListing x){var asset=db.GetAsset(x.AssetId);var property=asset is null?null:db.GetPropertyByAsset(asset.AssetId);return new MarketplaceListingView(x.ListingId,x.AssetId,asset?.AssetType??"unknown",asset?.Name??x.AssetId,x.ListingType,x.PriceAtomic,cfg.FromAtomic(x.PriceAtomic),cfg.CurrencySymbol,x.SellerType,DisplayOwner(db,x.SellerType,x.SellerId),property?.PropertyType,property?.RegionName,x.CreatedUtc);}
    private static string DisplayOwner(EconomyDatabase db,string ownerType,string ownerId){if(ownerType=="town"&&long.TryParse(ownerId,out var tid))return db.GetTownById(tid)?.Name??"Town";return ownerType=="player"?"Player":"System";}
}
