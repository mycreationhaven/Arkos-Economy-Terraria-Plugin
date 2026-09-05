namespace ArkoviaEconomy.Models;

public sealed record ArkoviaAsset(
    string AssetId,
    string AssetType,
    string Name,
    string OwnerType,
    string OwnerId,
    string Status,
    string MetadataJson,
    int Version,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AssetTransferRecord(
    long Id,
    string TransferKey,
    string AssetId,
    string FromOwnerType,
    string FromOwnerId,
    string ToOwnerType,
    string ToOwnerId,
    string Reason,
    string Actor,
    DateTime CreatedUtc);

public sealed record ArkoviaTown(
    string TownId,
    string AssetId,
    string Name,
    int FounderUserId,
    long TreasuryAccountId,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record TownMember(
    string MembershipKey,
    string TownId,
    int TShockUserId,
    string Role,
    string Status,
    DateTime JoinedUtc,
    DateTime UpdatedUtc);

public sealed record ArkoviaProperty(
    string PropertyId,
    string AssetId,
    string PropertyType,
    string WorldKey,
    string RegionName,
    string? TownId,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
