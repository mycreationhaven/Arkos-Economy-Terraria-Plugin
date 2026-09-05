using System.Data;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    public readonly record struct AccountBalanceChange(
        long Id,
        long WalletBefore,
        long WalletAfter,
        long BankBefore,
        long BankAfter);

    public readonly record struct LedgerWrite(
        string ExternalId,
        long? FromAccountId,
        long? ToAccountId,
        long AmountAtomic,
        string Type,
        string ReferenceType,
        string ReferenceId,
        string Description,
        string Actor);

    /// <summary>
    /// Atomically applies wallet/bank balance changes and their ledger rows.
    /// Every balance uses an optimistic before-value check, so a concurrent
    /// mutation aborts the entire settlement instead of producing a partial move.
    /// </summary>
    public void CommitAccountSettlement(
        IReadOnlyList<AccountBalanceChange> changes,
        IReadOnlyList<LedgerWrite> ledgerWrites)
    {
        if (changes.Count == 0)
            throw new InvalidOperationException("Settlement requires at least one balance change.");
        if (ledgerWrites.Count == 0)
            throw new InvalidOperationException("Settlement requires at least one ledger write.");

        using var connection = (IDbConnection)(Activator.CreateInstance(_db.GetType(), _db.ConnectionString)
            ?? throw new InvalidOperationException("Unable to open economy settlement connection."));
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int Execute(string sql, params object[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            for (var i = 0; i < values.Length; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + i;
                parameter.Value = values[i];
                command.Parameters.Add(parameter);
            }
            return command.ExecuteNonQuery();
        }

        var now = DateTime.UtcNow.ToString("O");

        foreach (var change in changes)
        {
            if (change.WalletAfter < 0 || change.BankAfter < 0)
                throw new InvalidOperationException("Settlement would make an account balance negative.");

            var rows = Execute(
                "UPDATE ArkoviaEconomyAccounts " +
                "SET WalletAtomic=@p0,BankAtomic=@p1,UpdatedUtc=@p2 " +
                "WHERE Id=@p3 AND WalletAtomic=@p4 AND BankAtomic=@p5",
                change.WalletAfter,
                change.BankAfter,
                now,
                change.Id,
                change.WalletBefore,
                change.BankBefore);

            if (rows != 1)
                throw new InvalidOperationException("Account changed during settlement; no funds were moved. Retry.");
        }

        foreach (var write in ledgerWrites)
        {
            if (string.IsNullOrWhiteSpace(write.ExternalId))
                throw new InvalidOperationException("Settlement ledger external ID is required.");
            if (write.AmountAtomic <= 0)
                throw new InvalidOperationException("Settlement ledger amount must be positive.");

            Execute(
                "INSERT INTO ArkoviaEconomyTransactions " +
                "(ExternalId,FromAccountId,ToAccountId,AmountAtomic,Type,ReferenceType,ReferenceId,Description,Actor,CreatedUtc) " +
                "VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                write.ExternalId,
                write.FromAccountId ?? 0,
                write.ToAccountId ?? 0,
                write.AmountAtomic,
                write.Type,
                write.ReferenceType,
                write.ReferenceId,
                write.Description,
                write.Actor,
                now);
        }

        transaction.Commit();
    }
}
