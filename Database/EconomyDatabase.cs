using System.Data;
using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Database;

public sealed class EconomyDatabase
{
    private readonly IDbConnection _db;

    public EconomyDatabase(IDbConnection db) => _db = db;

    public void EnsureSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());

        creator.EnsureTableStructure(new SqlTable("ArkoviaPlayerWallets",
            new SqlColumn("TShockUserId", MySqlDbType.Int32) { Primary = true },
            new SqlColumn("AccountId", MySqlDbType.VarChar, 32),
            new SqlColumn("AccountRS", MySqlDbType.VarChar, 64),
            new SqlColumn("PublicKey", MySqlDbType.VarChar, 128),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyAccounts",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("TShockUserId", MySqlDbType.Int32),
            new SqlColumn("AccountType", MySqlDbType.VarChar, 32),
            new SqlColumn("Name", MySqlDbType.VarChar, 128),
            new SqlColumn("WalletAtomic", MySqlDbType.Int64),
            new SqlColumn("BankAtomic", MySqlDbType.Int64),
            new SqlColumn("Frozen", MySqlDbType.Int32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyTransactions",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("ExternalId", MySqlDbType.VarChar, 160) { Unique = true },
            new SqlColumn("FromAccountId", MySqlDbType.Int64),
            new SqlColumn("ToAccountId", MySqlDbType.Int64),
            new SqlColumn("AmountAtomic", MySqlDbType.Int64),
            new SqlColumn("Type", MySqlDbType.VarChar, 64),
            new SqlColumn("ReferenceType", MySqlDbType.VarChar, 64),
            new SqlColumn("ReferenceId", MySqlDbType.VarChar, 160),
            new SqlColumn("Description", MySqlDbType.Text),
            new SqlColumn("Actor", MySqlDbType.VarChar, 128),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyFunding",
            new SqlColumn("ExternalKey", MySqlDbType.VarChar, 200) { Primary = true },
            new SqlColumn("EventId", MySqlDbType.VarChar, 80),
            new SqlColumn("BlockId", MySqlDbType.VarChar, 40),
            new SqlColumn("Height", MySqlDbType.Int32),
            new SqlColumn("Timestamp", MySqlDbType.Int32),
            new SqlColumn("ChangeAtomic", MySqlDbType.Int64),
            new SqlColumn("BalanceAtomic", MySqlDbType.Int64),
            new SqlColumn("EventType", MySqlDbType.VarChar, 64),
            new SqlColumn("CreditedAtomic", MySqlDbType.Int64),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyState",
            new SqlColumn("StateKey", MySqlDbType.VarChar, 80) { Primary = true },
            new SqlColumn("StateValue", MySqlDbType.Text),
            new SqlColumn("UpdatedUtc", MySqlDbType.VarChar, 40)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyShops",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("ShopKey", MySqlDbType.VarChar, 80),
            new SqlColumn("ItemId", MySqlDbType.Int32),
            new SqlColumn("Prefix", MySqlDbType.Int32),
            new SqlColumn("BuyPriceAtomic", MySqlDbType.Int64),
            new SqlColumn("SellPriceAtomic", MySqlDbType.Int64),
            new SqlColumn("Stock", MySqlDbType.Int32),
            new SqlColumn("Enabled", MySqlDbType.Int32)
        ));

        creator.EnsureTableStructure(new SqlTable("ArkoviaEconomyMarket",
            new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
            new SqlColumn("SellerUserId", MySqlDbType.Int32),
            new SqlColumn("ItemId", MySqlDbType.Int32),
            new SqlColumn("Prefix", MySqlDbType.Int32),
            new SqlColumn("Quantity", MySqlDbType.Int32),
            new SqlColumn("UnitPriceAtomic", MySqlDbType.Int64),
            new SqlColumn("Remaining", MySqlDbType.Int32),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("CreatedUtc", MySqlDbType.VarChar, 40),
            new SqlColumn("ExpiresUtc", MySqlDbType.VarChar, 40)
        ));
    }

    public ArkoviaPlayerWallet? GetPlayerWallet(int userId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaPlayerWallets WHERE TShockUserId=@0",
            userId);

        if (!r.Read())
            return null;

        return new ArkoviaPlayerWallet(
            r.Get<int>("TShockUserId"),
            r.Get<string>("AccountId"),
            r.Get<string>("AccountRS"),
            r.Get<string>("PublicKey"),
            DateTime.Parse(r.Get<string>("CreatedUtc"))
        );
    }

    public void CreatePlayerWallet(
        int userId,
        string accountId,
        string accountRs,
        string publicKey)
    {
        if (GetPlayerWallet(userId) is not null)
            throw new InvalidOperationException(
                "This TShock account already has an Arkovia wallet.");

        _db.Query(
            "INSERT INTO ArkoviaPlayerWallets " +
            "(TShockUserId,AccountId,AccountRS,PublicKey,CreatedUtc) " +
            "VALUES (@0,@1,@2,@3,@4)",
            userId,
            accountId,
            accountRs,
            publicKey,
            DateTime.UtcNow.ToString("O"));
    }

    public EconomyAccount? GetPlayerAccount(int userId)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaEconomyAccounts WHERE TShockUserId=@0 AND AccountType='player'",
            userId);

        return r.Read() ? ReadAccount(r) : null;
    }

    public EconomyAccount? GetAccountById(long id)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaEconomyAccounts WHERE Id=@0",
            id);

        return r.Read() ? ReadAccount(r) : null;
    }

    public EconomyAccount? GetSystemAccount(string name)
    {
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaEconomyAccounts WHERE AccountType='system' AND Name=@0",
            name);

        return r.Read() ? ReadAccount(r) : null;
    }

    public long CreateAccount(
        int? userId,
        string type,
        string name,
        long walletAtomic)
    {
        var now = DateTime.UtcNow.ToString("O");

        _db.Query(
            "INSERT INTO ArkoviaEconomyAccounts " +
            "(TShockUserId,AccountType,Name,WalletAtomic,BankAtomic,Frozen,CreatedUtc,UpdatedUtc) " +
            "VALUES (@0,@1,@2,@3,0,0,@4,@4)",
            userId ?? 0,
            type,
            name,
            walletAtomic,
            now);

        using var r = _db.QueryReader(
            "SELECT Id FROM ArkoviaEconomyAccounts " +
            "WHERE AccountType=@0 AND Name=@1 ORDER BY Id DESC",
            type,
            name);

        if (!r.Read())
            throw new InvalidOperationException(
                "Unable to read newly created account.");

        return r.Get<long>("Id");
    }

    public void SetBalances(
        long accountId,
        long walletAtomic,
        long bankAtomic)
        => _db.Query(
            "UPDATE ArkoviaEconomyAccounts " +
            "SET WalletAtomic=@0,BankAtomic=@1,UpdatedUtc=@2 WHERE Id=@3",
            walletAtomic,
            bankAtomic,
            DateTime.UtcNow.ToString("O"),
            accountId);

    public void SetFrozen(long accountId, bool frozen)
        => _db.Query(
            "UPDATE ArkoviaEconomyAccounts " +
            "SET Frozen=@0,UpdatedUtc=@1 WHERE Id=@2",
            frozen ? 1 : 0,
            DateTime.UtcNow.ToString("O"),
            accountId);

    public bool TransactionExists(string externalId)
    {
        using var r = _db.QueryReader(
            "SELECT Id FROM ArkoviaEconomyTransactions WHERE ExternalId=@0",
            externalId);

        return r.Read();
    }

    public void InsertTransaction(
        string externalId,
        long? fromId,
        long? toId,
        long amountAtomic,
        string type,
        string referenceType,
        string referenceId,
        string description,
        string actor)
        => _db.Query(
            "INSERT INTO ArkoviaEconomyTransactions " +
            "(ExternalId,FromAccountId,ToAccountId,AmountAtomic,Type,ReferenceType,ReferenceId,Description,Actor,CreatedUtc) " +
            "VALUES (@0,@1,@2,@3,@4,@5,@6,@7,@8,@9)",
            externalId,
            fromId ?? 0,
            toId ?? 0,
            amountAtomic,
            type,
            referenceType,
            referenceId,
            description,
            actor,
            DateTime.UtcNow.ToString("O"));

    public List<LedgerTransaction> GetTransactions(
        long accountId,
        int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 100);

        var list = new List<LedgerTransaction>();

        using var r = _db.QueryReader(
            $"SELECT * FROM ArkoviaEconomyTransactions " +
            $"WHERE FromAccountId=@0 OR ToAccountId=@0 " +
            $"ORDER BY Id DESC LIMIT {limit}",
            accountId);

        while (r.Read())
        {
            list.Add(new LedgerTransaction(
                r.Get<long>("Id"),
                r.Get<string>("ExternalId"),
                GetNullableLong(r, "FromAccountId"),
                GetNullableLong(r, "ToAccountId"),
                r.Get<long>("AmountAtomic"),
                r.Get<string>("Type"),
                r.Get<string>("ReferenceType"),
                r.Get<string>("ReferenceId"),
                r.Get<string>("Description"),
                r.Get<string>("Actor"),
                DateTime.Parse(r.Get<string>("CreatedUtc"))
            ));
        }

        return list;
    }

    public bool FundingExists(string externalKey)
    {
        using var r = _db.QueryReader(
            "SELECT ExternalKey FROM ArkoviaEconomyFunding WHERE ExternalKey=@0",
            externalKey);

        return r.Read();
    }

    public void InsertFunding(
        BlockchainFundingEntry e,
        long creditedAtomic)
        => _db.Query(
            "INSERT INTO ArkoviaEconomyFunding " +
            "(ExternalKey,EventId,BlockId,Height,Timestamp,ChangeAtomic,BalanceAtomic,EventType,CreditedAtomic,CreatedUtc) " +
            "VALUES (@0,@1,@2,@3,@4,@5,@6,@7,@8,@9)",
            e.ExternalKey,
            e.EventId,
            e.BlockId,
            e.Height,
            e.Timestamp,
            e.ChangeAtomic,
            e.BalanceAtomic,
            e.EventType,
            creditedAtomic,
            DateTime.UtcNow.ToString("O"));

    public string? GetState(string key)
    {
        using var r = _db.QueryReader(
            "SELECT StateValue FROM ArkoviaEconomyState WHERE StateKey=@0",
            key);

        return r.Read()
            ? r.Get<string>("StateValue")
            : null;
    }

    public void SetState(string key, string value)
    {
        if (GetState(key) is null)
        {
            _db.Query(
                "INSERT INTO ArkoviaEconomyState " +
                "(StateKey,StateValue,UpdatedUtc) VALUES (@0,@1,@2)",
                key,
                value,
                DateTime.UtcNow.ToString("O"));
        }
        else
        {
            _db.Query(
                "UPDATE ArkoviaEconomyState " +
                "SET StateValue=@0,UpdatedUtc=@1 WHERE StateKey=@2",
                value,
                DateTime.UtcNow.ToString("O"),
                key);
        }
    }

    private static EconomyAccount ReadAccount(QueryResult r)
        => new(
            r.Get<long>("Id"),
            GetNullableInt(r, "TShockUserId"),
            r.Get<string>("AccountType"),
            r.Get<string>("Name"),
            r.Get<long>("WalletAtomic"),
            r.Get<long>("BankAtomic"),
            r.Get<int>("Frozen") != 0,
            DateTime.Parse(r.Get<string>("CreatedUtc")),
            DateTime.Parse(r.Get<string>("UpdatedUtc"))
        );

    private static int? GetNullableInt(
        QueryResult r,
        string name)
    {
        try
        {
            var value = r.Get<int>(name);
            return value == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static long? GetNullableLong(
        QueryResult r,
        string name)
    {
        try
        {
            var value = r.Get<long>(name);
            return value == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }
}
