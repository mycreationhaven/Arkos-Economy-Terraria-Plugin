using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Rests;
using TShockAPI;

namespace ArkoviaEconomy.Api;

public sealed class MarketplacePlayerApi(EconomyDatabase db, PlayerTradingService trading) : IDisposable
{
    private bool _registered; private volatile bool _active=true;
    public void Register()
    {
        if(_registered)return;
        Register("Arkovia marketplace live inventory","/marketplace/api/v1/player/inventory/{subject}",Inventory,Permissions.MarketplaceApiRead);
        Register("Arkovia marketplace stocks","/marketplace/api/v1/stocks",Stocks,Permissions.MarketplaceApiRead);
        Register("Arkovia marketplace stock detail","/marketplace/api/v1/stocks/{ticker}",Stock,Permissions.MarketplaceApiRead);
        Register("Arkovia marketplace inventory list","/marketplace/api/v1/mutate/inventory-list/{subject}/{slot}/{quantity}/{priceAtomic}/{operationKey}",ListInventory,Permissions.MarketplaceApiWrite);
        Register("Arkovia marketplace stock buy","/marketplace/api/v1/mutate/stock-buy/{subject}/{ticker}/{shares}/{operationKey}",BuyStock,Permissions.MarketplaceApiWrite);
        Register("Arkovia marketplace item claim","/marketplace/api/v1/mutate/claim-items/{subject}/{operationKey}",ClaimItems,Permissions.MarketplaceApiWrite);
        _registered=true;
    }
    private void Register(string name,string path,RestCommandD callback,string permission){var c=new SecureRestCommand(name,path,callback,permission){DoLog=false};TShock.RestApi.Register(c);}
    private object Inventory(RestRequestArgs a){if(!_active)return Disabled();try{return new RestObject { ["inventory"] = trading.InventoryForSubject(a.Verbs["subject"]??"") };}catch(InvalidOperationException e){return new RestObject("409"){Error=e.Message};}}
    private object Stocks(RestRequestArgs a){if(!_active)return Disabled();var r=new RestObject();r["stocks"]=trading.Stocks().Select(x=>new {x.Ticker,x.Name,x.PriceAtomic,price=trading.Money(x.PriceAtomic),x.SharesOutstanding,x.SharesAvailable,x.UpdatedUtc}).ToList();return r;}
    private object Stock(RestRequestArgs a){if(!_active)return Disabled();var x=trading.Stock(a.Verbs["ticker"]??"");if(x is null)return new RestObject("404"){Error="Stock not found."};var r=new RestObject();r["stock"]=new {x.Ticker,x.Name,x.PriceAtomic,price=trading.Money(x.PriceAtomic),x.SharesOutstanding,x.SharesAvailable,x.UpdatedUtc};return r;}
    private object ListInventory(RestRequestArgs a){if(!_active)return Disabled();try{if(!int.TryParse(a.Verbs["slot"],out var slot)||!int.TryParse(a.Verbs["quantity"],out var qty)||!long.TryParse(a.Verbs["priceAtomic"],out var price))return new RestObject("400"){Error="Invalid listing values."};var l=trading.ListInventoryItem(a.Verbs["subject"]??"",slot,qty,price,a.Verbs["operationKey"]??"");var r=new RestObject();r["listingId"]=l.ListingId;r["assetId"]=l.AssetId;return r;}catch(InvalidOperationException e){return new RestObject("400"){Error=e.Message};}}
    private object BuyStock(RestRequestArgs a){if(!_active)return Disabled();try{var subject=a.Verbs["subject"]??"";var link=db.GetWebAccountLinkBySubject(subject)??throw new InvalidOperationException("No linked Terraria account was found.");if(!long.TryParse(a.Verbs["shares"],out var qty))throw new InvalidOperationException("Invalid share quantity.");var h=trading.BuyStock(link.TShockUserId,a.Verbs["ticker"]??"",qty,a.Verbs["operationKey"]??"",link.TShockAccountName);var r=new RestObject();r["holding"]=new {h.Ticker,h.Name,h.Shares,h.PriceAtomic,price=trading.Money(h.PriceAtomic),h.MarketValueAtomic,marketValue=trading.Money(h.MarketValueAtomic)};return r;}catch(InvalidOperationException e){return new RestObject("400"){Error=e.Message};}}
    private object ClaimItems(RestRequestArgs a){if(!_active)return Disabled();try{var link=db.GetWebAccountLinkBySubject(a.Verbs["subject"]??"")??throw new InvalidOperationException("No linked Terraria account was found.");var p=TShock.Players.FirstOrDefault(x=>x is {Active:true,IsLoggedIn:true}&&x.Account?.ID==link.TShockUserId)??throw new InvalidOperationException("Your Terraria character must be online to claim items.");var r=new RestObject();r["claimed"]=trading.ClaimItems(p);return r;}catch(InvalidOperationException e){return new RestObject("409"){Error=e.Message};}}
    private static RestObject Disabled()=>new("503"){Error="Marketplace player API unavailable."};
    public void Dispose()=>_active=false;
}
