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
}
