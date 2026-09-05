using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Newtonsoft.Json;
using TShockAPI;

namespace ArkoviaEconomy.Core;

public sealed class PlayerTradingService(EconomyDatabase db, MarketplaceService marketplace, Func<EconomyConfig> config)
{
    private readonly object _inventoryGate = new();

    public IReadOnlyList<InventoryMarketItem> InventoryForSubject(string subject)
    {
        var link=db.GetWebAccountLinkBySubject(subject)??throw new InvalidOperationException("No linked Terraria account was found.");
        var player=TShock.Players.FirstOrDefault(p=>p is {Active:true,IsLoggedIn:true} && p.Account?.ID==link.TShockUserId)
            ?? throw new InvalidOperationException("Your Terraria character must be online to view live inventory.");
        return player.TPlayer.inventory.Select((i,slot)=>(i,slot)).Where(x=>x.i is not null && !x.i.IsAir && x.i.stack>0)
            .Select(x=>new InventoryMarketItem(x.slot,x.i.type,x.i.Name,x.i.stack,x.i.prefix,x.i.favorited,x.i.maxStack)).ToList();
    }

    public MarketplaceListing ListInventoryItem(string subject,int slot,int quantity,long priceAtomic,string operationKey)
    {
        var link=db.GetWebAccountLinkBySubject(subject)??throw new InvalidOperationException("No linked Terraria account was found.");
        if(priceAtomic<=0) throw new InvalidOperationException("Price must be positive.");
        if(slot<0||slot>=59) throw new InvalidOperationException("Invalid inventory slot.");
        if(quantity<=0) throw new InvalidOperationException("Quantity must be positive.");
        lock(_inventoryGate)
        {
            var player=TShock.Players.FirstOrDefault(p=>p is {Active:true,IsLoggedIn:true} && p.Account?.ID==link.TShockUserId)
                ?? throw new InvalidOperationException("Your Terraria character must be online to list inventory.");
            var item=player.TPlayer.inventory[slot];
            if(item is null||item.IsAir||item.stack<quantity) throw new InvalidOperationException("That inventory slot changed. Refresh and try again.");
            if(item.favorited) throw new InvalidOperationException("Favorited items cannot be listed. Unfavorite the item first.");
            var metadata=JsonConvert.SerializeObject(new {itemId=item.type,itemName=item.Name,prefix=(int)item.prefix,quantity,source="terraria_inventory"});
            var asset=db.CreateAsset("item",quantity==1?item.Name:$"{item.Name} x{quantity}","player",link.TShockUserId.ToString(),metadata);
            try
            {
                db.CreateItemEscrow(asset.AssetId,item.type,item.Name,item.prefix,quantity,link.TShockUserId);
                var listing=marketplace.ListPlayerAsset(link.TShockUserId,link.TShockAccountName,asset.AssetId,priceAtomic);
                item.stack-=quantity;
                if(item.stack<=0) item.TurnToAir();
                player.SendData(PacketTypes.PlayerSlot,"",player.Index,slot);
                EconomyLog.Info($"[ArkoviaEconomy] Inventory escrow created. User={link.TShockUserId}, Asset={asset.AssetId}, Item={item.type}, Qty={quantity}.");
                return listing;
            }
            catch
            {
                try { db.AbandonItemEscrow(asset.AssetId, link.TShockUserId); } catch { }
                throw;
            }
        }
    }

    public int ClaimItems(TSPlayer player)
    {
        if(!player.IsLoggedIn||player.Account is null) throw new InvalidOperationException("You must be logged in.");
        var items=db.GetClaimableItems(player.Account.ID); var count=0;
        foreach(var item in items)
        {
            EconomyLog.Info($"[ArkoviaEconomy] Delivering marketplace item asset {item.AssetId} to user {player.Account.ID}: {item.ItemName} x{item.Quantity}.");
            player.GiveItem(item.ItemId,item.Quantity,item.Prefix);
            db.MarkItemDelivered(item.AssetId); count++;
        }
        return count;
    }

    public IReadOnlyList<StockQuote> Stocks()=>db.GetStocks();
    public StockQuote? Stock(string ticker)=>db.GetStock(ticker);
    public IReadOnlyList<StockHoldingView> Holdings(int userId)=>db.GetStockHoldings(userId);
    public StockHoldingView BuyStock(int userId,string ticker,long shares,string operationKey,string actor)=>db.BuyStock(userId,ticker,shares,operationKey,actor);
    public decimal Money(long atomic)=>config().FromAtomic(atomic);
}
