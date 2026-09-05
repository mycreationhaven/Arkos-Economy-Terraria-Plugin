using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using TShockAPI;

namespace ArkoviaEconomy.Commands;

public sealed class StockCommands(PlayerTradingService trading, EconomyDatabase db, Func<EconomyConfig> config)
{
    public IEnumerable<Command> Build()
    {
        yield return new Command(Permissions.Market, Stocks, "stocks", "stock");
        yield return new Command(Permissions.Admin, Admin, "stockadmin");
    }

    private void Stocks(CommandArgs a)
    {
        if(!a.Player.IsLoggedIn||a.Player.Account is null){a.Player.SendErrorMessage("You must be logged in.");return;}
        if(a.Parameters.Count==0||a.Parameters[0].Equals("market",StringComparison.OrdinalIgnoreCase))
        {
            var q=trading.Stocks(); if(q.Count==0){a.Player.SendInfoMessage("No stocks are listed yet.");return;}
            a.Player.SendInfoMessage("Stock market: "+string.Join(" | ",q.Select(x=>$"{x.Ticker} {config().FromAtomic(x.PriceAtomic)} {config().CurrencySymbol} ({x.SharesAvailable} avail.)"))); return;
        }
        var sub=a.Parameters[0].ToLowerInvariant();
        if(sub is "mine" or "portfolio")
        {
            var h=trading.Holdings(a.Player.Account.ID); if(h.Count==0){a.Player.SendInfoMessage("You do not own any stocks yet.");return;}
            a.Player.SendInfoMessage("Your stocks: "+string.Join(" | ",h.Select(x=>$"{x.Ticker} x{x.Shares} @ {config().FromAtomic(x.PriceAtomic)} = {config().FromAtomic(x.MarketValueAtomic)} {config().CurrencySymbol}"))); return;
        }
        if(sub=="buy"&&a.Parameters.Count>=3&&long.TryParse(a.Parameters[2],out var qty))
        {
            try { var h=trading.BuyStock(a.Player.Account.ID,a.Parameters[1],qty,Guid.NewGuid().ToString("N"),a.Player.Name); a.Player.SendSuccessMessage($"Bought {qty} shares of {h.Ticker}. You now own {h.Shares} shares."); }
            catch(Exception ex){a.Player.SendErrorMessage(ex.Message);} return;
        }
        a.Player.SendInfoMessage("/stocks [market|mine] or /stocks buy <ticker> <shares>");
    }

    private void Admin(CommandArgs a)
    {
        if(!a.Player.IsLoggedIn||a.Player.Account is null){a.Player.SendErrorMessage("Log in first.");return;}
        try
        {
            if(a.Parameters.Count>=5&&a.Parameters[0].Equals("create",StringComparison.OrdinalIgnoreCase)&&decimal.TryParse(a.Parameters[^2],out var price)&&long.TryParse(a.Parameters[^1],out var shares))
            {
                var account=db.GetPlayerAccount(a.Player.Account.ID)??throw new InvalidOperationException("Economy account not found.");
                var ticker=a.Parameters[1]; var name=string.Join(" ",a.Parameters.Skip(2).Take(a.Parameters.Count-4));
                db.CreateStock(ticker,name,account.Id,config().ToAtomic(price),shares); a.Player.SendSuccessMessage($"Created stock {ticker.ToUpperInvariant()} with {shares} shares at {price} {config().CurrencySymbol}."); return;
            }
            if(a.Parameters.Count==3&&a.Parameters[0].Equals("price",StringComparison.OrdinalIgnoreCase)&&decimal.TryParse(a.Parameters[2],out var next))
            { db.SetStockPrice(a.Parameters[1],config().ToAtomic(next)); a.Player.SendSuccessMessage("Stock price updated."); return; }
        }
        catch(Exception ex){a.Player.SendErrorMessage(ex.Message);return;}
        a.Player.SendInfoMessage("/stockadmin create <ticker> <name> <price> <shares> | /stockadmin price <ticker> <price>");
    }
}
