using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Microsoft.Data.Sqlite;

internal static class TradingTests
{
    public static int Run()
    {
        var checks=0; void Check(bool x){if(!x)throw new Exception("Trading test failed");checks++;}
        var path=Path.Combine(Path.GetTempPath(),$"arkovia-trading-{Guid.NewGuid():N}.sqlite");
        using var c=new SqliteConnection($"Data Source={path}");c.Open();var db=new EconomyDatabase(c);db.EnsureSchema();var cfg=new EconomyConfig();var eco=new EconomyService(db,()=>cfg);
        var issuer=eco.GetOrCreatePlayer(701,"Issuer");var buyer=eco.GetOrCreatePlayer(702,"Buyer");db.SetBalances(buyer.Id,cfg.ToAtomic(100),0);db.SetBalances(issuer.Id,0,0);
        db.CreateStock("ARKX","Arkovia Exchange",issuer.Id,cfg.ToAtomic(2),25);Check(db.GetStock("ARKX")!.SharesAvailable==25);
        var h=db.BuyStock(702,"ARKX",3,"test-buy-1","Buyer");Check(h.Shares==3);Check(db.GetStock("ARKX")!.SharesAvailable==22);Check(db.GetPlayerAccount(702)!.WalletAtomic==cfg.ToAtomic(94));Check(db.GetAccountById(issuer.Id)!.WalletAtomic==cfg.ToAtomic(6));
        try{db.BuyStock(702,"ARKX",3,"test-buy-1","Buyer");throw new Exception("Expected duplicate operation rejection");}catch{checks++;}
        Check(db.GetStockHoldings(702).Single().Shares==3);Check(db.GetPlayerAccount(702)!.WalletAtomic==cfg.ToAtomic(94));
        var a=db.CreateAsset("item","Stone x10","player","702","{}");db.CreateItemEscrow(a.AssetId,3,"Stone",0,10,702);Check(db.GetClaimableItems(702).Count==1);db.MarkItemDelivered(a.AssetId);Check(db.GetClaimableItems(702).Count==0);
        c.Close();SqliteConnection.ClearAllPools();File.Delete(path);return checks;
    }
}
