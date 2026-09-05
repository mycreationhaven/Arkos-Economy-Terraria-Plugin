using System.Data;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using ArkoviaEconomy.Models;
using TShockAPI;
using TShockAPI.DB;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    private void EnsureSettlementSchema()
    {
        var creator = new SqlTableCreator(_db, _db.GetSqlQueryBuilder());
        creator.EnsureTableStructure(new SqlTable("ArkoviaOperations",
            new SqlColumn("OperationId", MySqlDbType.VarChar, 120) { Primary = true },
            new SqlColumn("Kind", MySqlDbType.VarChar, 32),
            new SqlColumn("UserId", MySqlDbType.Int32),
            new SqlColumn("Status", MySqlDbType.VarChar, 32),
            new SqlColumn("Payload", MySqlDbType.Text)));
        EnsureAssetSchema();
        EnsureTownSchema();
    }

    public EconomyOperation? GetOperation(string id)
    {
        using var r = _db.QueryReader("SELECT Payload FROM ArkoviaOperations WHERE OperationId=@0", id);
        return r.Read() ? JsonConvert.DeserializeObject<EconomyOperation>(r.Get<string>("Payload")) : null;
    }
    public List<EconomyOperation> Operations(string kind, int? userId = null)
    {
        var result = new List<EconomyOperation>();
        using var r = userId is int user
            ? _db.QueryReader("SELECT Payload FROM ArkoviaOperations WHERE Kind=@0 AND UserId=@1", kind, user)
            : _db.QueryReader("SELECT Payload FROM ArkoviaOperations WHERE Kind=@0", kind);
        while (r.Read()) result.Add(JsonConvert.DeserializeObject<EconomyOperation>(r.Get<string>("Payload"))!);
        return result;
    }
    public T Atomic<T>(Func<SettlementUnit, T> action)
    {
        using var connection = (IDbConnection)Activator.CreateInstance(_db.GetType(), _db.ConnectionString)!;
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var result = action(new SettlementUnit(connection, transaction));
        transaction.Commit();
        return result;
    }
}

public sealed class SettlementUnit(IDbConnection connection, IDbTransaction transaction)
{
    private IDbCommand Command(string sql, params object[] values)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@p" + i;
            p.Value = values[i];
            cmd.Parameters.Add(p);
        }
        return cmd;
    }

    public int Execute(string sql, params object[] values)
    {
        using var cmd = Command(sql, values);
        return cmd.ExecuteNonQuery();
    }

    public long ScalarLong(string sql, params object[] values)
    {
        using var cmd = Command(sql, values);
        var value = cmd.ExecuteScalar();
        if (value is null || value is DBNull)
            throw new InvalidOperationException("Expected database value was not found.");
        return Convert.ToInt64(value);
    }

    public void Insert(EconomyOperation op) => Execute(
        "INSERT INTO ArkoviaOperations (OperationId,Kind,UserId,Status,Payload) VALUES (@p0,@p1,@p2,@p3,@p4)",
        op.Id, op.Kind, op.UserId, op.Status, JsonConvert.SerializeObject(op));
    public void Update(EconomyOperation op, string previousStatus)
    {
        if (Execute("UPDATE ArkoviaOperations SET Status=@p0,Payload=@p1 WHERE OperationId=@p2 AND Status=@p3",
            op.Status, JsonConvert.SerializeObject(op), op.Id, previousStatus) != 1)
            throw new InvalidOperationException("Operation changed; retry.");
    }
    public void Wallet(EconomyAccount account, long next)
    {
        if (next < 0 || Execute("UPDATE ArkoviaEconomyAccounts SET WalletAtomic=@p0,UpdatedUtc=@p1 WHERE Id=@p2 AND WalletAtomic=@p3 AND Frozen=0",
            next, DateTime.UtcNow.ToString("O"), account.Id, account.WalletAtomic) != 1)
            throw new InvalidOperationException("Insufficient balance, frozen account or concurrent change.");
    }
    public void Ledger(string id, long? from, long? to, long amount, string kind, string reference, string actor)
    {
        Execute("INSERT INTO ArkoviaEconomyTransactions (ExternalId,FromAccountId,ToAccountId,AmountAtomic,Type,ReferenceType,ReferenceId,Description,Actor,CreatedUtc) " +
            "VALUES (@p0,@p1,@p2,@p3,@p4,@p4,@p5,@p5,@p6,@p7)",
            id, from ?? 0, to ?? 0, amount, kind, reference, actor, DateTime.UtcNow.ToString("O"));
    }
}
