using System.Globalization;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Terraria;
using TShockAPI;

namespace ArkoviaEconomy.Commands;

public sealed class MarketplaceCommands
{
    private readonly MarketplaceService _market;
    private readonly TownService _towns;
    private readonly EconomyDatabase _db;
    private readonly ConfigManager _config;

    public MarketplaceCommands(
        MarketplaceService market,
        TownService towns,
        EconomyDatabase db,
        ConfigManager config)
    {
        _market = market;
        _towns = towns;
        _db = db;
        _config = config;
    }

    public IEnumerable<Command> Build()
    {
        yield return new Command(Permissions.Market, Market, "market")
        {
            AllowServer = false,
            HelpText = "/market listings|info|sellproperty|buy|cancel"
        };
    }

    private static (int Id, string Name) RequireIdentity(CommandArgs args)
    {
        if (!args.Player.RealPlayer || !args.Player.IsLoggedIn || args.Player.Account is null)
            throw new InvalidOperationException("You must be logged into a TShock account.");
        return (args.Player.Account.ID, args.Player.Account.Name);
    }

    private long ParseAmount(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new InvalidOperationException("Enter a positive price.");
        return _config.Current.ToAtomic(amount);
    }

    private void Market(CommandArgs args)
    {
        try
        {
            var identity = RequireIdentity(args);
            _market.CleanupExpiredReservations();
            if (args.Parameters.Count == 0)
            {
                Help(args);
                return;
            }

            switch (args.Parameters[0].ToLowerInvariant())
            {
                case "listings":
                case "list":
                {
                    var listings = _db.GetMarketplaceListings("active", 20);
                    if (listings.Count == 0)
                    {
                        args.Player.SendInfoMessage("There are no active Arkovia marketplace listings.");
                        break;
                    }
                    foreach (var listing in listings)
                    {
                        var asset = _db.GetAsset(listing.AssetId);
                        var label = asset is null ? listing.AssetId : $"{asset.Name} ({asset.AssetType})";
                        args.Player.SendInfoMessage(
                            $"{listing.ListingId} | {label} | {_config.Current.Format(listing.PriceAtomic)}");
                    }
                    break;
                }
                case "info":
                {
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /market info <listing ID>");
                    var listing = _db.GetMarketplaceListing(args.Parameters[1])
                        ?? throw new InvalidOperationException("Listing was not found.");
                    var asset = _db.GetAsset(listing.AssetId)
                        ?? throw new InvalidOperationException("Listing asset was not found.");
                    args.Player.SendInfoMessage(
                        $"{listing.ListingId} | {asset.Name} ({asset.AssetType}) | {_config.Current.Format(listing.PriceAtomic)} | {listing.Status}");
                    args.Player.SendInfoMessage(
                        $"Asset: {asset.AssetId} | seller: {listing.SellerOwnerType}:{listing.SellerOwnerId}");
                    break;
                }
                case "sellproperty":
                case "sell-property":
                {
                    if (args.Parameters.Count < 3)
                        throw new InvalidOperationException("Usage: /market sellproperty <TShock region name> <price>");
                    var price = ParseAmount(args.Parameters[^1]);
                    var regionName = string.Join(" ", args.Parameters.Skip(1).Take(args.Parameters.Count - 2));
                    var town = _towns.RequireTownForUser(identity.Id);
                    _towns.RequireMayor(town, identity.Id);
                    var property = _db.GetPropertyByRegion(Main.worldID.ToString(), regionName)
                        ?? throw new InvalidOperationException("No active Arkovia property is bound to that region.");
                    if (property.TownId != town.TownId)
                        throw new InvalidOperationException("That property does not belong to your town.");
                    var listing = _market.ListTownProperty(town, identity.Id, property.AssetId, price, _towns);
                    args.Player.SendSuccessMessage(
                        $"Listed {property.RegionName} for {_config.Current.Format(price)}.");
                    args.Player.SendInfoMessage($"Listing ID: {listing.ListingId}");
                    break;
                }
                case "buy":
                case "buynow":
                {
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /market buy <listing ID>");
                    var operationKey = $"market-buy:{identity.Id}:{args.Parameters[1]}:{Guid.NewGuid():N}";
                    var sale = _market.BuyNowForPlayer(args.Parameters[1], identity.Id, identity.Name, operationKey);
                    args.Player.SendSuccessMessage(
                        $"Purchase completed for {_config.Current.Format(sale.AmountAtomic)}.");
                    args.Player.SendInfoMessage($"Sale ID: {sale.SaleId} | Asset: {sale.AssetId}");
                    break;
                }
                case "cancel":
                {
                    if (args.Parameters.Count != 2)
                        throw new InvalidOperationException("Usage: /market cancel <listing ID>");
                    _market.CancelListing(args.Parameters[1], identity.Id, _towns);
                    args.Player.SendSuccessMessage("Marketplace listing cancelled and asset unlocked.");
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

    private static void Help(CommandArgs args)
    {
        args.Player.SendInfoMessage("/market listings | /market info <listing ID> | /market buy <listing ID>");
        args.Player.SendInfoMessage("/market sellproperty <region> <price> | /market cancel <listing ID>");
    }
}
