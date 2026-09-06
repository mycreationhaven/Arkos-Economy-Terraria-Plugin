using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Rests;
using TShockAPI;

namespace ArkoviaEconomy.Api;

/// <summary>Marketplace API intended for the trusted web backend.</summary>
public sealed class MarketplaceReadApi(EconomyDatabase db, Func<EconomyConfig> config, MarketplaceAccountLinkService links, PlayerTradingService? trading = null) : IDisposable
{
    private volatile bool _active = true;
    private bool _registered;

    public void Register()
    {
        if (_registered || !config().Api.EnablePublicReadApi) return;
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace status", "/marketplace/api/v1/status", Status, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace live players", "/marketplace/api/v1/players", Players, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace listings", "/marketplace/api/v1/listings", Listings, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace listing", "/marketplace/api/v1/listings/{listingId}", Listing, Permissions.MarketplaceApiRead));
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace linked account", "/marketplace/api/v1/me/{subject}", Me, Permissions.MarketplaceApiRead) { DoLog = false });
        TShock.RestApi.Register(new SecureRestCommand("Arkovia marketplace account link", "/marketplace/api/v1/link/{account}/{code}/{subject}", LinkAccount, Permissions.MarketplaceApiLink) { DoLog = false });
        _registered = true;
        if (!TShock.Config.Settings.RestApiEnabled) EconomyLog.Warn("[ArkoviaEconomy] Marketplace API routes registered, but TShock RestApiEnabled is false.");
    }

    private object Status(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var cfg = config();
        var result = new RestObject();
        result["api"] = "arkovia-marketplace"; result["version"] = 1; result["currency"] = cfg.CurrencySymbol; result["decimals"] = cfg.Decimals; result["readOnly"] = true;
        result["onlinePlayers"] = TShock.Players.Count(p => p is { Active: true });
        return result;
    }

    private object Players(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        var players = TShock.Players.Where(p => p is { Active: true }).Select(p => new
        {
            name = string.IsNullOrWhiteSpace(p.Name) ? "Adventurer" : p.Name,
            location = "Arkovia",
            loggedIn = p.IsLoggedIn
        }).ToList();
        var result = new RestObject(); result["count"] = players.Count; result["players"] = players; return result;
    }

    private object Listings(RestRequestArgs args) { if (!_active) return Disabled(); var views = MarketplaceReadProjection.GetActiveListings(db, config(), ParseLimit(args.Parameters["limit"])); var r = new RestObject(); r["count"] = views.Count; r["listings"] = views; return r; }
    private object Listing(RestRequestArgs args) { if (!_active) return Disabled(); var id=(args.Verbs["listingId"]??"").Trim(); if(id.Length is <10 or >64||!id.StartsWith("ARK-LIST-",StringComparison.Ordinal))return new RestObject("400"){Error="Invalid listing ID."}; var view=MarketplaceReadProjection.GetActiveListing(db,config(),id); if(view is null)return new RestObject("404"){Error="Listing was not found or is no longer available."}; var r=new RestObject();r["listing"]=view;return r; }
    private object Me(RestRequestArgs args) { if(!_active)return Disabled();var subject=(args.Verbs["subject"]??"").Trim();var link=db.GetWebAccountLinkBySubject(subject);if(link is null)return new RestObject("404"){Error="No linked Terraria account was found."};var cfg=config();var ownerId=link.TShockUserId.ToString();var r=new RestObject();r["linked"]=true;r["accountName"]=link.TShockAccountName;r["linkedUtc"]=link.LinkedUtc;r["sellableAssets"]=MarketplaceReadProjection.GetPlayerSellableAssets(db,ownerId,100);r["listings"]=MarketplaceReadProjection.GetPlayerListings(db,cfg,ownerId,100);r["purchases"]=MarketplaceReadProjection.GetPlayerPurchases(db,cfg,ownerId,100);r["stocks"]=trading?.Holdings(link.TShockUserId).Select(x=>new{x.Ticker,x.Name,x.Shares,x.PriceAtomic,price=cfg.FromAtomic(x.PriceAtomic),x.MarketValueAtomic,marketValue=cfg.FromAtomic(x.MarketValueAtomic)}).ToList()??[];return r; }
    private object LinkAccount(RestRequestArgs args) { if(!_active)return Disabled();try{var link=links.Redeem(args.Verbs["account"]??"",args.Verbs["code"]??"",args.Verbs["subject"]??"");var r=new RestObject();r["linkId"]=link.LinkId;r["tshockUserId"]=link.TShockUserId;r["accountName"]=link.TShockAccountName;r["webSubject"]=link.WebSubject;r["linkedUtc"]=link.LinkedUtc;return r;}catch(InvalidOperationException ex){return new RestObject("400"){Error=ex.Message};} }
    private static int ParseLimit(string? value)=>string.IsNullOrWhiteSpace(value)?50:int.TryParse(value,out var n)?Math.Clamp(n,1,100):50;
    private static RestObject Disabled()=>new("503"){Error="Marketplace API is unavailable until the plugin is restarted."};
    public void Dispose()=>_active=false;
}

public sealed record MarketplaceListingView(string ListingId,string AssetId,string AssetType,string AssetName,string ListingType,long PriceAtomic,decimal Price,string Currency,string SellerType,string SellerName,string? PropertyType,string? RegionName,DateTime CreatedUtc);
public sealed record MarketplaceSellableAssetView(string AssetId,string AssetType,string AssetName,int Version,DateTime UpdatedUtc);
public sealed record MarketplaceUserListingView(string ListingId,string AssetId,string AssetType,string AssetName,string ListingType,long PriceAtomic,decimal Price,string Currency,string Status,DateTime? ReservedUntilUtc,DateTime CreatedUtc,DateTime UpdatedUtc);
public sealed record MarketplacePurchaseView(string SaleId,string ListingId,string AssetId,string AssetType,string AssetName,long AmountAtomic,decimal Amount,string Currency,string SellerType,string SellerName,DateTime PurchasedUtc);

public static class MarketplaceReadProjection
{
    public static IReadOnlyList<MarketplaceListingView> GetActiveListings(EconomyDatabase db,EconomyConfig config,int limit=50){limit=Math.Clamp(limit,1,100);var result=new List<MarketplaceListingView>();foreach(var listing in db.GetMarketplaceListings("active",limit)){var view=ProjectActiveListing(db,config,listing);if(view is not null)result.Add(view);}return result;}
    public static MarketplaceListingView? GetActiveListing(EconomyDatabase db,EconomyConfig config,string listingId){var listing=db.GetMarketplaceListing(listingId);if(listing is null||!string.Equals(listing.Status,"active",StringComparison.OrdinalIgnoreCase))return null;return ProjectActiveListing(db,config,listing);}
    public static IReadOnlyList<MarketplaceSellableAssetView> GetPlayerSellableAssets(EconomyDatabase db,string playerOwnerId,int limit=50){var result=new List<MarketplaceSellableAssetView>();foreach(var asset in db.GetAssetsForOwner("player",playerOwnerId,"active",limit)){if(asset.AssetType is "town" or "land" or "property")continue;result.Add(new MarketplaceSellableAssetView(asset.AssetId,asset.AssetType,asset.Name,asset.Version,asset.UpdatedUtc));}return result;}
    public static IReadOnlyList<MarketplaceUserListingView> GetPlayerListings(EconomyDatabase db,EconomyConfig config,string playerOwnerId,int limit=50){var result=new List<MarketplaceUserListingView>();foreach(var listing in db.GetMarketplaceListingsForOwner("player",playerOwnerId,limit)){var asset=db.GetAsset(listing.AssetId);result.Add(new MarketplaceUserListingView(listing.ListingId,listing.AssetId,asset?.AssetType??"unknown",asset?.Name??"Unknown asset",listing.ListingType,listing.PriceAtomic,config.FromAtomic(listing.PriceAtomic),config.CurrencySymbol,listing.Status,listing.ReservedUntilUtc,listing.CreatedUtc,listing.UpdatedUtc));}return result;}
    public static IReadOnlyList<MarketplacePurchaseView> GetPlayerPurchases(EconomyDatabase db,EconomyConfig config,string playerOwnerId,int limit=50){var result=new List<MarketplacePurchaseView>();foreach(var sale in db.GetMarketplaceSalesForBuyer("player",playerOwnerId,limit)){var asset=db.GetAsset(sale.AssetId);var listing=db.GetMarketplaceListing(sale.ListingId);var sellerName=string.Equals(sale.SellerOwnerType,"town",StringComparison.OrdinalIgnoreCase)?db.GetTown(sale.SellerOwnerId)?.Name??"Town":listing is null?"Player":db.GetAccountById(listing.SellerAccountId)?.Name??"Player";result.Add(new MarketplacePurchaseView(sale.SaleId,sale.ListingId,sale.AssetId,asset?.AssetType??"unknown",asset?.Name??"Unknown asset",sale.AmountAtomic,config.FromAtomic(sale.AmountAtomic),config.CurrencySymbol,sale.SellerOwnerType,sellerName,sale.CreatedUtc));}return result;}
    private static MarketplaceListingView? ProjectActiveListing(EconomyDatabase db,EconomyConfig config,MarketplaceListing listing){var asset=db.GetAsset(listing.AssetId);if(asset is null||!string.Equals(asset.Status,"listed",StringComparison.OrdinalIgnoreCase)||asset.Version!=listing.AssetVersion||!string.Equals(asset.OwnerType,listing.SellerOwnerType,StringComparison.OrdinalIgnoreCase)||!string.Equals(asset.OwnerId,listing.SellerOwnerId,StringComparison.Ordinal))return null;string sellerName;if(string.Equals(listing.SellerOwnerType,"town",StringComparison.OrdinalIgnoreCase))sellerName=db.GetTown(listing.SellerOwnerId)?.Name??"Town";else sellerName=db.GetAccountById(listing.SellerAccountId)?.Name??"Player";var property=db.GetPropertyByAsset(listing.AssetId);return new MarketplaceListingView(listing.ListingId,listing.AssetId,asset.AssetType,asset.Name,listing.ListingType,listing.PriceAtomic,config.FromAtomic(listing.PriceAtomic),config.CurrencySymbol,listing.SellerOwnerType,sellerName,property?.PropertyType,property?.RegionName,listing.CreatedUtc);}
}
