using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Database;

public sealed partial class EconomyDatabase
{
    public void SetTownMemberRole(string townId, int userId, string expectedRole, string nextRole)
    {
        nextRole = nextRole.Trim().ToLowerInvariant();
        if (nextRole is not ("resident" or "assistant" or "mayor"))
            throw new InvalidOperationException("Unsupported town role.");

        var member = GetTownMember(townId, userId)
            ?? throw new InvalidOperationException("That account is not an active town member.");
        if (!string.Equals(member.Role, expectedRole, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Town membership changed; refresh and retry.");

        var updated = _db.Query(
            "UPDATE ArkoviaTownMembers SET Role=@0,UpdatedUtc=@1 " +
            "WHERE MembershipKey=@2 AND Status='active' AND Role=@3",
            nextRole, DateTime.UtcNow.ToString("O"), member.MembershipKey, member.Role);
        if (updated != 1)
            throw new InvalidOperationException("Town membership changed; refresh and retry.");
    }

    public void KickTownMember(string townId, int userId)
    {
        var member = GetTownMember(townId, userId)
            ?? throw new InvalidOperationException("That account is not an active town member.");
        if (string.Equals(member.Role, "mayor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The mayor cannot be kicked. Transfer leadership first.");

        var updated = _db.Query(
            "UPDATE ArkoviaTownMembers SET Status='kicked',UpdatedUtc=@0 " +
            "WHERE MembershipKey=@1 AND Status='active' AND Role=@2",
            DateTime.UtcNow.ToString("O"), member.MembershipKey, member.Role);
        if (updated != 1)
            throw new InvalidOperationException("Town membership changed; refresh and retry.");
    }

    public void TransferTownLeadership(string townId, int currentMayorUserId, int nextMayorUserId)
    {
        if (currentMayorUserId == nextMayorUserId)
            throw new InvalidOperationException("That account is already the mayor.");

        var current = GetTownMember(townId, currentMayorUserId)
            ?? throw new InvalidOperationException("Current mayor membership was not found.");
        var next = GetTownMember(townId, nextMayorUserId)
            ?? throw new InvalidOperationException("The new mayor must already be an active town member.");
        if (!string.Equals(current.Role, "mayor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Leadership changed; refresh and retry.");

        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaTownMembers SET Role='assistant',UpdatedUtc=@p0 " +
                "WHERE MembershipKey=@p1 AND Status='active' AND Role='mayor'",
                now, current.MembershipKey) != 1)
                throw new InvalidOperationException("Leadership changed; no transfer was made. Retry.");

            if (unit.Execute(
                "UPDATE ArkoviaTownMembers SET Role='mayor',UpdatedUtc=@p0 " +
                "WHERE MembershipKey=@p1 AND Status='active' AND Role=@p2",
                now, next.MembershipKey, next.Role) != 1)
                throw new InvalidOperationException("Target membership changed; no transfer was made. Retry.");

            unit.Execute(
                "UPDATE ArkoviaTowns SET UpdatedUtc=@p0 WHERE TownId=@p1 AND Status='active'",
                now, townId);
            return 0;
        });
    }

    public IReadOnlyList<ArkoviaProperty> GetTownProperties(string townId)
    {
        var result = new List<ArkoviaProperty>();
        using var r = _db.QueryReader(
            "SELECT * FROM ArkoviaProperties WHERE TownId=@0 AND Status='active' ORDER BY CreatedUtc", townId);
        while (r.Read()) result.Add(ReadProperty(r));
        return result;
    }

    public void UnclaimTownProperty(string townId, string worldKey, string regionName)
    {
        var property = GetPropertyByRegion(worldKey, regionName)
            ?? throw new InvalidOperationException("No active Arkovia property is bound to that TShock region.");
        if (!string.Equals(property.TownId, townId, StringComparison.Ordinal))
            throw new InvalidOperationException("That property does not belong to your town.");
        var asset = GetAsset(property.AssetId)
            ?? throw new InvalidOperationException("Property asset record is missing.");
        if (!string.Equals(asset.OwnerType, "town", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(asset.OwnerId, townId, StringComparison.Ordinal))
            throw new InvalidOperationException("Property ownership is inconsistent; an administrator must review it.");

        var now = DateTime.UtcNow.ToString("O");
        Atomic(unit =>
        {
            if (unit.Execute(
                "UPDATE ArkoviaProperties SET Status='unclaimed',UpdatedUtc=@p0 " +
                "WHERE PropertyId=@p1 AND TownId=@p2 AND Status='active'",
                now, property.PropertyId, townId) != 1)
                throw new InvalidOperationException("Property changed; no unclaim was made. Retry.");

            if (unit.Execute(
                "UPDATE ArkoviaAssets SET Status='retired',Version=Version+1,UpdatedUtc=@p0 " +
                "WHERE AssetId=@p1 AND OwnerType='town' AND OwnerId=@p2 AND Status='active' AND Version=@p3",
                now, asset.AssetId, townId, asset.Version) != 1)
                throw new InvalidOperationException("Property asset changed; no unclaim was made. Retry.");
            return 0;
        });
    }
}
