using System.Globalization;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Terraria;
using TShockAPI;

namespace ArkoviaEconomy.Commands;

public sealed class TownCommands
{
    private readonly TownService _towns;
    private readonly EconomyDatabase _db;
    private readonly ConfigManager _config;

    public TownCommands(TownService towns, EconomyDatabase db, ConfigManager config)
    {
        _towns = towns;
        _db = db;
        _config = config;
    }

    public IEnumerable<Command> Build()
    {
        yield return new Command(Permissions.Town, Town, "town")
        {
            AllowServer = false,
            HelpText = "/town create|info|invite|accept|leave|balance|deposit|withdraw|claim"
        };
        yield return new Command(Permissions.Property, Property, "property")
        {
            AllowServer = false,
            HelpText = "/property info <region>"
        };
    }

    private static (int Id, string Name) RequireIdentity(CommandArgs args)
    {
        if (!args.Player.RealPlayer || !args.Player.IsLoggedIn || args.Player.Account is null)
            throw new InvalidOperationException("You must be logged into a TShock account.");
        return (args.Player.Account.ID, args.Player.Account.Name);
    }

    private static void RequirePermission(CommandArgs args, string permission)
    {
        if (!args.Player.HasPermission(permission))
            throw new InvalidOperationException("Missing permission: " + permission);
    }

    private long ParseAmount(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new InvalidOperationException("Enter a positive amount.");
        return _config.Current.ToAtomic(amount);
    }

    private void Town(CommandArgs args)
    {
        try
        {
            var identity = RequireIdentity(args);
            if (args.Parameters.Count == 0)
            {
                Help(args);
                return;
            }

            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "create":
                {
                    RequirePermission(args, Permissions.TownCreate);
                    if (args.Parameters.Count < 2)
                        throw new InvalidOperationException("Usage: /town create <name>");
                    var name = string.Join(" ", args.Parameters.Skip(1)).Trim();
                    var town = _towns.CreateTown(identity.Id, name);
                    args.Player.SendSuccessMessage($"Town created: {town.Name}");
                    args.Player.SendInfoMessage($"Town ID: {town.TownId}");
                    args.Player.SendInfoMessage($"Asset ID: {town.AssetId}");
                    break;
                }
                case "info":
                {
                    var town = args.Parameters.Count > 1
                        ? _db.GetTown(string.Join(" ", args.Parameters.Skip(1)))
                        : _db.GetTownForUser(identity.Id);
                    if (town is null) throw new InvalidOperationException("Town not found.");
                    var treasury = _towns.GetTreasuryAccount(town);
                    var members = _db.GetTownMembers(town.TownId);
                    args.Player.SendInfoMessage($"{town.Name} | members: {members.Count} | treasury: {_config.Current.Format(treasury.WalletAtomic)}");
                    args.Player.SendInfoMessage($"Town ID: {town.TownId} | Asset: {town.AssetId}");
                    break;
                }
                case "invite":
                {
                    RequirePermission(args, Permissions.TownManage);
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /town invite <TShockAccount>");
                    var town = _towns.RequireTownForUser(identity.Id);
                    var target = TShock.UserAccounts.GetUserAccountByName(args.Parameters[1])
                        ?? throw new InvalidOperationException("TShock account not found.");
                    var invite = _towns.Invite(town, identity.Id, target.ID);
                    args.Player.SendSuccessMessage($"Invited {target.Name} to {town.Name}. Invitation expires {invite.ExpiresUtc:u}.");
                    break;
                }
                case "accept":
                case "join":
                {
                    if (args.Parameters.Count < 2)
                        throw new InvalidOperationException("Usage: /town accept <town name or ID>");
                    var townName = string.Join(" ", args.Parameters.Skip(1));
                    _towns.AcceptInvite(townName, identity.Id);
                    var joined = _towns.RequireTownForUser(identity.Id);
                    args.Player.SendSuccessMessage($"You joined {joined.Name}.");
                    break;
                }
                case "leave":
                    _towns.Leave(identity.Id);
                    args.Player.SendSuccessMessage("You left your town.");
                    break;
                case "balance":
                {
                    var town = _towns.RequireTownForUser(identity.Id);
                    var treasury = _towns.GetTreasuryAccount(town);
                    args.Player.SendInfoMessage($"{town.Name} treasury: {_config.Current.Format(treasury.WalletAtomic)}");
                    break;
                }
                case "deposit":
                {
                    RequirePermission(args, Permissions.TownBank);
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /town deposit <amount>");
                    var town = _towns.RequireTownForUser(identity.Id);
                    var amount = ParseAmount(args.Parameters[1]);
                    _towns.Deposit(town, identity.Id, identity.Name, amount);
                    args.Player.SendSuccessMessage($"Deposited {_config.Current.Format(amount)} into {town.Name}.");
                    break;
                }
                case "withdraw":
                {
                    RequirePermission(args, Permissions.TownBank);
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /town withdraw <amount>");
                    var town = _towns.RequireTownForUser(identity.Id);
                    var amount = ParseAmount(args.Parameters[1]);
                    _towns.Withdraw(town, identity.Id, identity.Name, amount);
                    args.Player.SendSuccessMessage($"Withdrew {_config.Current.Format(amount)} from {town.Name}.");
                    break;
                }
                case "claim":
                {
                    RequirePermission(args, Permissions.TownClaim);
                    if (args.Parameters.Count < 2)
                        throw new InvalidOperationException("Usage: /town claim <TShock region name>");
                    var town = _towns.RequireTownForUser(identity.Id);
                    var regionName = string.Join(" ", args.Parameters.Skip(1));
                    var property = _towns.ClaimRegion(town, identity.Id, identity.Name, regionName,
                        args.Player.HasPermission(Permissions.AdminTown));
                    args.Player.SendSuccessMessage($"Claimed region {property.RegionName} for {town.Name}.");
                    args.Player.SendInfoMessage($"Property ID: {property.PropertyId} | Asset: {property.AssetId}");
                    break;
                }
                case "help":
                default:
                    Help(args);
                    break;
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private void Property(CommandArgs args)
    {
        try
        {
            RequireIdentity(args);
            if (args.Parameters.Count < 2 || !args.Parameters[0].Equals("info", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Usage: /property info <TShock region name>");
            var regionName = string.Join(" ", args.Parameters.Skip(1));
            var property = _db.GetPropertyByRegion(Main.worldID.ToString(), regionName)
                ?? throw new InvalidOperationException("No Arkovia property is bound to that region.");
            var asset = _db.GetAsset(property.AssetId)
                ?? throw new InvalidOperationException("Property asset record is missing.");
            args.Player.SendInfoMessage($"{property.RegionName} | type: {property.PropertyType} | owner: {asset.OwnerType}:{asset.OwnerId}");
            args.Player.SendInfoMessage($"Property ID: {property.PropertyId} | Asset: {asset.AssetId}");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage(ex.Message);
        }
    }

    private static void Help(CommandArgs args)
    {
        args.Player.SendInfoMessage("/town create <name> | /town info [name] | /town invite <account> | /town accept <town>");
        args.Player.SendInfoMessage("/town leave | /town balance | /town deposit <amount> | /town withdraw <amount> | /town claim <region>");
    }
}
