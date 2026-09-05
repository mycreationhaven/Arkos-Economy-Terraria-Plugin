namespace ArkoviaEconomy.Models;

public sealed record InventoryMarketItem(
    int Slot,
    int ItemId,
    string Name,
    int Stack,
    int Prefix,
    bool Favorited,
    int MaxStack);

public sealed record StockQuote(
    string Ticker,
    string Name,
    long PriceAtomic,
    long SharesOutstanding,
    long SharesAvailable,
    long IssuerAccountId,
    DateTime UpdatedUtc);

public sealed record StockHoldingView(
    string Ticker,
    string Name,
    long Shares,
    long PriceAtomic,
    long MarketValueAtomic);

public sealed record ItemEscrowRecord(
    string AssetId,
    int ItemId,
    string ItemName,
    int Prefix,
    int Quantity,
    int OriginalOwnerUserId,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
