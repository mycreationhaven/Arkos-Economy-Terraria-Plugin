namespace ArkoviaEconomy.Models;

public sealed record EconomyAccount(
    long Id,
    int? TShockUserId,
    string AccountType,
    string Name,
    long WalletAtomic,
    long BankAtomic,
    bool Frozen,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record LedgerTransaction(
    long Id,
    string ExternalId,
    long? FromAccountId,
    long? ToAccountId,
    long AmountAtomic,
    string Type,
    string ReferenceType,
    string ReferenceId,
    string Description,
    string Actor,
    DateTime CreatedUtc);

public sealed record BlockchainFundingEntry(
    string ExternalKey,
    string EventId,
    string BlockId,
    int Height,
    int Timestamp,
    long ChangeAtomic,
    long BalanceAtomic,
    string EventType);

public sealed record ShopItem(
    long Id,
    string ShopKey,
    int ItemId,
    int Prefix,
    long BuyPriceAtomic,
    long SellPriceAtomic,
    int? Stock,
    bool Enabled);

public sealed record MarketListing(
    long Id,
    int SellerUserId,
    int ItemId,
    int Prefix,
    int Quantity,
    long UnitPriceAtomic,
    int Remaining,
    string Status,
    DateTime CreatedUtc,
    DateTime ExpiresUtc);
