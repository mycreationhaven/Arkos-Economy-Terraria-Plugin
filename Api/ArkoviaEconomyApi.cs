using ArkoviaEconomy.Core;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Api;

/// <summary>
/// In-process API for other TShock plugins. No blockchain private keys are ever required.
/// </summary>
public sealed class ArkoviaEconomyApi
{
    private readonly EconomyService _economy;
    public static ArkoviaEconomyApi? Instance { get; internal set; }

    public event EventHandler<EconomyTransactionEventArgs>? TransactionCompleted;

    internal ArkoviaEconomyApi(EconomyService economy) => _economy = economy;

    public EconomyAccount GetOrCreatePlayer(int tshockUserId, string accountName) => _economy.GetOrCreatePlayer(tshockUserId, accountName);
    public EconomyAccount GetTreasury() => _economy.GetTreasury();

    public LedgerTransaction Transfer(int fromUserId, string fromName, int toUserId, string toName, long amountAtomic, string referenceType, string referenceId, string description, string actor)
    {
        var from = _economy.GetOrCreatePlayer(fromUserId, fromName);
        var to = _economy.GetOrCreatePlayer(toUserId, toName);
        var tx = _economy.Transfer(from, to, amountAtomic, "plugin_transfer", referenceType, referenceId, description, actor);
        TransactionCompleted?.Invoke(this, new EconomyTransactionEventArgs(tx));
        return tx;
    }

    public void RewardFromTreasury(int userId, string userName, long amountAtomic, string referenceType, string referenceId, string description, string actor = "plugin")
    {
        var treasury = _economy.GetTreasury();
        var player = _economy.GetOrCreatePlayer(userId, userName);
        var tx = _economy.Transfer(treasury, player, amountAtomic, "reward", referenceType, referenceId, description, actor);
        TransactionCompleted?.Invoke(this, new EconomyTransactionEventArgs(tx));
    }
}

public sealed class EconomyTransactionEventArgs : EventArgs
{
    public LedgerTransaction Transaction { get; }
    public EconomyTransactionEventArgs(LedgerTransaction transaction) => Transaction = transaction;
}
