namespace ArkoviaEconomy.Models;

public sealed record MarketplaceListing(
    string ListingId,
    string AssetId,
    string SellerOwnerType,
    string SellerOwnerId,
    long SellerAccountId,
    string ListingType,
    long PriceAtomic,
    string Status,
    int AssetVersion,
    string ReservedByOwnerType,
    string ReservedByOwnerId,
    string ReservationKey,
    DateTime? ReservedUntilUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record MarketplaceEscrow(
    string EscrowId,
    string ListingId,
    string ReservationKey,
    string BuyerOwnerType,
    string BuyerOwnerId,
    long BuyerAccountId,
    long AmountAtomic,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record MarketplaceSale(
    string SaleId,
    string ListingId,
    string AssetId,
    string SellerOwnerType,
    string SellerOwnerId,
    string BuyerOwnerType,
    string BuyerOwnerId,
    long AmountAtomic,
    string TransferKey,
    DateTime CreatedUtc);
