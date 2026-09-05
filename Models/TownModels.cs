namespace ArkoviaEconomy.Models;

public sealed record TownInvite(
    string InviteKey,
    string TownId,
    int TShockUserId,
    int InvitedByUserId,
    string Status,
    DateTime ExpiresUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
