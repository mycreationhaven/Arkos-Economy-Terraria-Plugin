using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using Terraria;
using TShockAPI;

namespace ArkoviaEconomy.Core;

public sealed class TownService
{
    private readonly EconomyDatabase _db;
    private readonly EconomyService _economy;
    private readonly object _gate = new();

    public TownService(EconomyDatabase db, EconomyService economy)
    {
        _db = db;
        _economy = economy;
    }

    public ArkoviaTown CreateTown(int founderUserId, string name)
    {
        lock (_gate)
            return _db.CreateTownBundle(name, founderUserId);
    }

    public ArkoviaTown RequireTownForUser(int userId)
        => _db.GetTownForUser(userId)
           ?? throw new InvalidOperationException("You are not a member of an Arkovia town.");

    public TownMember RequireMember(ArkoviaTown town, int userId)
        => _db.GetTownMember(town.TownId, userId)
           ?? throw new InvalidOperationException("You are not an active member of that town.");

    public TownMember RequireManager(ArkoviaTown town, int userId)
    {
        var member = RequireMember(town, userId);
        if (member.Role is not ("mayor" or "assistant"))
            throw new InvalidOperationException("Only the mayor or an assistant can manage this town.");
        return member;
    }

    public TownMember RequireMayor(ArkoviaTown town, int userId)
    {
        var member = RequireMember(town, userId);
        if (!string.Equals(member.Role, "mayor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the town mayor can do that.");
        return member;
    }

    public TownInvite Invite(ArkoviaTown town, int actorUserId, int targetUserId)
    {
        RequireManager(town, actorUserId);
        if (actorUserId == targetUserId)
            throw new InvalidOperationException("You cannot invite yourself.");
        lock (_gate)
            return _db.CreateTownInvite(town.TownId, targetUserId, actorUserId, TimeSpan.FromHours(24));
    }

    public void AcceptInvite(string townIdOrName, int userId)
    {
        var town = _db.GetTown(townIdOrName)
            ?? throw new InvalidOperationException("Town not found.");
        lock (_gate)
            _db.AcceptTownInvite(town.TownId, userId);
    }

    public void Leave(int userId)
    {
        var town = RequireTownForUser(userId);
        lock (_gate)
            _db.LeaveTown(town.TownId, userId);
    }

    public void Promote(ArkoviaTown town, int actorUserId, int targetUserId)
    {
        RequireMayor(town, actorUserId);
        if (actorUserId == targetUserId)
            throw new InvalidOperationException("The mayor cannot promote themselves.");
        var target = RequireMember(town, targetUserId);
        if (string.Equals(target.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That member is already an assistant.");
        if (!string.Equals(target.Role, "resident", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only residents can be promoted to assistant.");
        lock (_gate)
            _db.SetTownMemberRole(town.TownId, targetUserId, target.Role, "assistant");
    }

    public void Demote(ArkoviaTown town, int actorUserId, int targetUserId)
    {
        RequireMayor(town, actorUserId);
        var target = RequireMember(town, targetUserId);
        if (!string.Equals(target.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only assistants can be demoted.");
        lock (_gate)
            _db.SetTownMemberRole(town.TownId, targetUserId, target.Role, "resident");
    }

    public void Kick(ArkoviaTown town, int actorUserId, int targetUserId)
    {
        RequireManager(town, actorUserId);
        if (actorUserId == targetUserId)
            throw new InvalidOperationException("Use /town leave to leave your town.");
        var actor = RequireMember(town, actorUserId);
        var target = RequireMember(town, targetUserId);
        if (string.Equals(target.Role, "mayor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The mayor cannot be kicked.");
        if (string.Equals(actor.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.Role, "resident", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Assistants may only kick residents.");
        lock (_gate)
            _db.KickTownMember(town.TownId, targetUserId);
    }

    public void TransferLeadership(ArkoviaTown town, int actorUserId, int targetUserId)
    {
        RequireMayor(town, actorUserId);
        RequireMember(town, targetUserId);
        lock (_gate)
            _db.TransferTownLeadership(town.TownId, actorUserId, targetUserId);
    }

    public EconomyAccount GetTreasuryAccount(ArkoviaTown town)
        => _db.GetAccountById(town.TreasuryAccountId)
           ?? throw new InvalidOperationException("Town treasury account was not found.");

    public void Deposit(ArkoviaTown town, int userId, string userName, long amountAtomic)
    {
        RequireMember(town, userId);
        var player = _economy.GetOrCreatePlayer(userId, userName);
        var treasury = GetTreasuryAccount(town);
        _economy.Transfer(player, treasury, amountAtomic, "town_deposit", "town", town.TownId,
            $"Deposit to {town.Name}", userName);
    }

    public void Withdraw(ArkoviaTown town, int userId, string userName, long amountAtomic)
    {
        RequireMayor(town, userId);
        var player = _economy.GetOrCreatePlayer(userId, userName);
        var treasury = GetTreasuryAccount(town);
        _economy.Transfer(treasury, player, amountAtomic, "town_withdrawal", "town", town.TownId,
            $"Withdrawal from {town.Name}", userName);
    }

    public ArkoviaProperty ClaimRegion(
        ArkoviaTown town,
        int actorUserId,
        string actorAccountName,
        string regionName,
        bool adminOverride = false)
    {
        RequireManager(town, actorUserId);
        var region = TShock.Regions.GetRegionByName(regionName)
            ?? throw new InvalidOperationException("TShock region not found in this world.");

        if (!adminOverride && !string.Equals(region.Owner, actorAccountName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You must own the TShock region before claiming it for your town.");

        var worldKey = Main.worldID.ToString();
        lock (_gate)
            return _db.CreateTownProperty(town.TownId, "land", worldKey, region.Name, region.Name);
    }

    public void UnclaimRegion(ArkoviaTown town, int actorUserId, string regionName)
    {
        RequireMayor(town, actorUserId);
        lock (_gate)
            _db.UnclaimTownProperty(town.TownId, Main.worldID.ToString(), regionName);
    }
}
