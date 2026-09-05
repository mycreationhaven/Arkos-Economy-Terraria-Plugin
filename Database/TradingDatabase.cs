using ArkoviaEconomy.Models;
using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureTradingSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaItemEscrow",
            new SqlColumn("AssetId", MySqlDbType.VarChar, 64) { Primary = true },
            new SqlColumn("ItemId", MySqlDbType.Int32),
            new SqlColumn("ItemName", MySqlDbType.VarChar, 128),
            new SqlColumn("Prefix", MySqlDbType.Int32),
            new SqlColumn("Quantity", MySqlDbType.Int32),
            new SqlColumn("OriginalOwnerUserId", MySqlDbType.Int32),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaStocks",
            new SqlColumn("Ticker", MySqlDbType.VarChar, 12) { Primary = true },
            new SqlColumn("Name", MySqlDbType.VarChar, 128),
            new SqlColumn("IssuerAccountId", MySqlDbType.Int64),
            new SqlColumn("PriceAtomic", MySqlDbType.Int64),
            new SqlColumn("SharesOutstanding", MySqlDbType.Int64),
            new SqlColumn("SharesAvailable", MySqlDbType.Int64),
            new SqlColumn("Active", MySqlDbType.Int32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));

        creator.EnsureTableStructure(new SqlTable("ArkoviaStockHoldings",
            new SqlColumn("HoldingKey", MySqlDbType.VarChar, 96) { Primary = true },
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("Ticker", MySqlDbType.VarChar, 12),
            new SqlColumn("Shares", MySqlDbType.Int64),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)));
    }

    public void CreateItemEscrow(string assetId, int itemId, string itemName, int prefix, int quantity, int ownerUserId)
    {
        var now = DateTime.UtcNow.ToString("O");
        _db.Query("INSERT INTO ArkoviaItemEscrow (AssetId,ItemId,ItemName,Prefix,Quantity,OriginalOwnerUserId,Status,CreatedUtc,UpdatedUtc) VALUES (@0,@1,@2,@3,@4,@5,'escrowed',@6,@6)",
            assetId, itemId, itemName, prefix, quantity, ownerUserId, now);
    }

    public ItemEscrowRecord? GetItemEscrow(string assetId)
    {
        using var r = _db.QueryReader("SELECT * FROM ArkoviaItemEscrow WHERE AssetId=@0", assetId);
        if (!r.Read()) return null;
        return new ItemEscrowRecord(r.Get<string>("AssetId"), r.Get<int>("ItemId"), r.Get<string>("ItemName"), r.Get<int>("Prefix"), r.Get<int>("Quantity"), r.Get<int>("OriginalOwnerUserId"), r.Get<string>("Status"), DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc")));
    }

    public IReadOnlyList<ItemEscrowRecord> GetClaimableItems(int userId)
    {
        var result = new List<ItemEscrowRecord>();
        using var r = _db.QueryReader("SELECT e.* FROM ArkoviaItemEscrow e JOIN ArkoviaAssets a ON a.AssetId=e.AssetId WHERE e.Status='escrowed' AND a.OwnerType='player' AND a.OwnerId=@0 AND a.Status='active' ORDER BY e.CreatedUtc", userId.ToString());
        while (r.Read()) result.Add(new ItemEscrowRecord(r.Get<string>("AssetId"), r.Get<int>("ItemId"), r.Get<string>("ItemName"), r.Get<int>("Prefix"), r.Get<int>("Quantity"), r.Get<int>("OriginalOwnerUserId"), r.Get<string>("Status"), DateTime.Parse(r.Get<string>("CreatedUtc")), DateTime.Parse(r.Get<string>("UpdatedUtc"))));
        return result;
    }

    public void MarkItemDelivered(string assetId)
    {
        var now=DateTime.UtcNow.ToString("O");
        Atomic(unit => {
            if (unit.Execute("UPDATE ArkoviaItemEscrow SET Status='delivered',UpdatedUtc=@p0 WHERE AssetId=@p1 AND Status='escrowed'", now, assetId) != 1)
                throw new InvalidOperationException("Item escrow is no longer claimable.");
            if (unit.Execute("UPDATE ArkoviaAssets SET Status='consumed',Version=Version+1,UpdatedUtc=@p0 WHERE AssetId=@p1 AND Status='active'", now, assetId) != 1)
                throw new InvalidOperationException("Item asset is no longer claimable.");
            return 0;
        });
    }

    public void AbandonItemEscrow(string assetId, int ownerUserId)
    {
        Atomic(unit => {
            unit.Execute("DELETE FROM ArkoviaItemEscrow WHERE AssetId=@p0 AND OriginalOwnerUserId=@p1 AND Status='escrowed'", assetId, ownerUserId);
            unit.Execute("DELETE FROM ArkoviaAssets WHERE AssetId=@p0 AND OwnerType='player' AND OwnerId=@p1 AND Status='active'", assetId, ownerUserId.ToString());
            return 0;
        });
    }

    public IReadOnlyList<StockQuote> GetStocks(bool activeOnly=true)
    {
        var result=new List<StockQuote>();
        using var r = activeOnly ? _db.QueryReader("SELECT * FROM ArkoviaStocks WHERE Active=1 ORDER BY Ticker") : _db.QueryReader("SELECT * FROM ArkoviaStocks ORDER BY Ticker");
        while(r.Read()) result.Add(ReadStock(r));
        return result;
    }

    public StockQuote? GetStock(string ticker)
    {
        using var r=_db.QueryReader("SELECT * FROM ArkoviaStocks WHERE Ticker=@0 AND Active=1", ticker.ToUpperInvariant());
        return r.Read()?ReadStock(r):null;
    }

    private static StockQuote ReadStock(QueryResult r) => new(r.Get<string>("Ticker"),r.Get<string>("Name"),r.Get<long>("PriceAtomic"),r.Get<long>("SharesOutstanding"),r.Get<long>("SharesAvailable"),r.Get<long>("IssuerAccountId"),DateTime.Parse(r.Get<string>("UpdatedUtc")));

    public void CreateStock(string ticker,string name,long issuerAccountId,long priceAtomic,long shares)
    {
        if(priceAtomic<=0||shares<=0) throw new InvalidOperationException("Price and shares must be positive.");
        ticker=ticker.Trim().ToUpperInvariant();
        if(ticker.Length is <1 or >12 || !ticker.All(c=>char.IsLetterOrDigit(c)||c=='.')) throw new InvalidOperationException("Ticker must be 1-12 letters/numbers/dots.");
        var now=DateTime.UtcNow.ToString("O");
        _db.Query("INSERT INTO ArkoviaStocks (Ticker,Name,IssuerAccountId,PriceAtomic,SharesOutstanding,SharesAvailable,Active,CreatedUtc,UpdatedUtc) VALUES (@0,@1,@2,@3,@4,@4,1,@5,@5)",ticker,name.Trim(),issuerAccountId,priceAtomic,shares,now);
    }

    public void SetStockPrice(string ticker,long priceAtomic)
    {
        if(priceAtomic<=0) throw new InvalidOperationException("Price must be positive.");
        if(_db.Query("UPDATE ArkoviaStocks SET PriceAtomic=@0,UpdatedUtc=@1 WHERE Ticker=@2 AND Active=1",priceAtomic,DateTime.UtcNow.ToString("O"),ticker.Trim().ToUpperInvariant())!=1)
            throw new InvalidOperationException("Stock not found.");
    }

    public IReadOnlyList<StockHoldingView> GetStockHoldings(int userId)
    {
        var result=new List<StockHoldingView>();
        using var r=_db.QueryReader("SELECT h.Ticker,s.Name,h.Shares,s.PriceAtomic FROM ArkoviaStockHoldings h JOIN ArkoviaStocks s ON s.Ticker=h.Ticker WHERE h.TShockUserId=@0 AND h.Shares>0 ORDER BY h.Ticker",userId);
        while(r.Read()) { var shares=r.Get<long>("Shares"); var price=r.Get<long>("PriceAtomic"); result.Add(new StockHoldingView(r.Get<string>("Ticker"),r.Get<string>("Name"),shares,price,checked(shares*price))); }
        return result;
    }

    public StockHoldingView BuyStock(int userId,string ticker,long shares,string operationKey,string actor)
    {
        if(shares<=0||shares>1_000_000) throw new InvalidOperationException("Share quantity must be between 1 and 1,000,000.");
        ticker=ticker.Trim().ToUpperInvariant();
        var stock=GetStock(ticker)??throw new InvalidOperationException("Stock not found.");
        var buyer=GetPlayerAccount(userId)??throw new InvalidOperationException("Economy account not found.");
        var issuer=GetAccountById(stock.IssuerAccountId)??throw new InvalidOperationException("Stock issuer account not found.");
        var cost=checked(stock.PriceAtomic*shares);
        if(buyer.Id==issuer.Id) throw new InvalidOperationException("Issuer cannot buy its own primary offering.");
        if(buyer.WalletAtomic<cost) throw new InvalidOperationException("Insufficient wallet balance.");
        if(stock.SharesAvailable<shares) throw new InvalidOperationException("Not enough shares are available.");
        var now=DateTime.UtcNow.ToString("O");
        var key=$"{userId}:{ticker}";
        Atomic(unit=>{
            if(unit.Execute("UPDATE ArkoviaStocks SET SharesAvailable=SharesAvailable-@p0,UpdatedUtc=@p1 WHERE Ticker=@p2 AND Active=1 AND SharesAvailable>=@p0 AND PriceAtomic=@p3",shares,now,ticker,stock.PriceAtomic)!=1)
                throw new InvalidOperationException("Stock quote changed or shares are no longer available. Refresh and retry.");
            unit.Wallet(buyer,buyer.WalletAtomic-cost);
            unit.Wallet(issuer,checked(issuer.WalletAtomic+cost));
            if(unit.Execute("UPDATE ArkoviaStockHoldings SET Shares=Shares+@p0,UpdatedUtc=@p1 WHERE HoldingKey=@p2",shares,now,key)==0)
                unit.Execute("INSERT INTO ArkoviaStockHoldings (HoldingKey,TShockUserId,Ticker,Shares,UpdatedUtc) VALUES (@p0,@p1,@p2,@p3,@p4)",key,userId,ticker,shares,now);
            unit.Ledger("stock-buy:"+operationKey,buyer.Id,issuer.Id,cost,"stock_purchase",ticker,actor);
            return 0;
        });
        return GetStockHoldings(userId).First(x=>x.Ticker==ticker);
    }
}
