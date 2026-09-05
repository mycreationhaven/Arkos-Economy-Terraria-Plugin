using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Rests;
using TShockAPI;

namespace ArkoviaEconomy.Api;

/// <summary>
/// Trusted-backend-only marketplace mutation API. The browser must never receive
/// the TShock REST token or choose the authoritative Terraria identity.
/// </summary>
public sealed class MarketplaceMutationApi(
    EconomyDatabase db,
    Func<EconomyConfig> config,
    MarketplaceWebMutationService mutations) : IDisposable
{
    private volatile bool _active = true;
    private bool _registered;

    public void Register()
    {
        if (_registered || !config().Api.EnablePublicReadApi)
            return;

        var list = new SecureRestCommand(
            "Arkovia marketplace web list",
            "/marketplace/api/v1/mutate/list/{subject}/{assetId}/{priceAtomic}/{operationKey}",
            List,
            Permissions.MarketplaceApiWrite)
        {
            DoLog = false
        };
        TShock.RestApi.Register(list);

        var buy = new SecureRestCommand(
            "Arkovia marketplace web buy",
            "/marketplace/api/v1/mutate/buy/{subject}/{listingId}/{operationKey}",
            Buy,
            Permissions.MarketplaceApiWrite)
        {
            DoLog = false
        };
        TShock.RestApi.Register(buy);

        var cancel = new SecureRestCommand(
            "Arkovia marketplace web cancel",
            "/marketplace/api/v1/mutate/cancel/{subject}/{listingId}/{operationKey}",
            Cancel,
            Permissions.MarketplaceApiWrite)
        {
            DoLog = false
        };
        TShock.RestApi.Register(cancel);

        _registered = true;
    }

    private object List(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        try
        {
            var subject = args.Parameters["subject"] ?? string.Empty;
            var assetId = args.Parameters["assetId"] ?? string.Empty;
            var operationKey = args.Parameters["operationKey"] ?? string.Empty;
            if (!long.TryParse(args.Parameters["priceAtomic"], out var priceAtomic) || priceAtomic <= 0)
                return new RestObject("400") { Error = "Invalid listing price." };

            var operation = mutations.List(subject, assetId, priceAtomic, operationKey);
            var listing = db.GetMarketplaceListing(operation.ResultId);
            if (listing is null)
                return new RestObject("500") { Error = "Marketplace listing result could not be loaded." };

            var result = new RestObject();
            result["operationKey"] = operation.OperationKey;
            result["status"] = operation.Status;
            result["listingId"] = listing.ListingId;
            result["assetId"] = listing.AssetId;
            result["priceAtomic"] = listing.PriceAtomic;
            result["price"] = config().FromAtomic(listing.PriceAtomic);
            result["currency"] = config().CurrencySymbol;
            return result;
        }
        catch (InvalidOperationException ex)
        {
            return new RestObject("400") { Error = ex.Message };
        }
    }

    private object Buy(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        try
        {
            var subject = args.Parameters["subject"] ?? string.Empty;
            var listingId = args.Parameters["listingId"] ?? string.Empty;
            var operationKey = args.Parameters["operationKey"] ?? string.Empty;
            var operation = mutations.Buy(subject, listingId, operationKey);
            var sale = db.GetMarketplaceSaleByListing(listingId);

            var result = new RestObject();
            result["operationKey"] = operation.OperationKey;
            result["status"] = operation.Status;
            result["listingId"] = operation.ListingId;
            result["saleId"] = operation.ResultId;
            if (sale is not null)
            {
                result["amountAtomic"] = sale.AmountAtomic;
                result["amount"] = config().FromAtomic(sale.AmountAtomic);
                result["currency"] = config().CurrencySymbol;
            }
            return result;
        }
        catch (InvalidOperationException ex)
        {
            return new RestObject("400") { Error = ex.Message };
        }
    }

    private object Cancel(RestRequestArgs args)
    {
        if (!_active) return Disabled();
        try
        {
            var subject = args.Parameters["subject"] ?? string.Empty;
            var listingId = args.Parameters["listingId"] ?? string.Empty;
            var operationKey = args.Parameters["operationKey"] ?? string.Empty;
            var operation = mutations.Cancel(subject, listingId, operationKey);

            var result = new RestObject();
            result["operationKey"] = operation.OperationKey;
            result["status"] = operation.Status;
            result["listingId"] = operation.ListingId;
            return result;
        }
        catch (InvalidOperationException ex)
        {
            return new RestObject("400") { Error = ex.Message };
        }
    }

    private static RestObject Disabled() =>
        new("503") { Error = "Marketplace mutation API is unavailable until the plugin is restarted." };

    public void Dispose() => _active = false;
}
