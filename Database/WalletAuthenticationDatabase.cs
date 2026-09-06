using ArkoviaEconomy.Models;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    public ArkoviaPlayerWallet? GetPlayerWalletByAddress(string accountRs)
    {
        accountRs = (accountRs ?? string.Empty).Trim();
        if (accountRs.Length is < 3 or > 64)
            return null;

        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaPlayerWallets WHERE UPPER(AccountRS)=UPPER(@0) LIMIT 1",
            accountRs);
        if (!r.Read())
            return null;

        return new ArkoviaPlayerWallet(
            r.Get<int>("TShockUserId"),
            r.Get<string>("AccountId"),
            r.Get<string>("AccountRS"),
            r.Get<string>("PublicKey"),
            DateTime.Parse(r.Get<string>("CreatedUtc")));
    }
}
