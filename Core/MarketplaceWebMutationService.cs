using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Core;

public sealed record MarketplaceWebMutationResult(
    string OperationKey,
    string Kind,
    string ListingId,
    string Status,
    string ResultId);

public sealed class MarketplaceWebMutationService
{
    private readonly EconomyDatabase _db;
    private readonly MarketplaceService _marketplace;
    private readonly TownService _towns;
    private readonly object _gate = new();

    public MarketplaceWebMutationService(
        EconomyDatabase db,
        MarketplaceService marketplace,
        TownService towns)
    {
        _db = db;
        _marketplace = marketplace;
        _towns = towns;
    }

    public MarketplaceWebMutationResult Buy(string webSubject, string listingId, string operationKey)
    {
        lock (_gate)
        {
            var link = RequireLink(webSubject);
            var operation = PrepareOperation(operationKey, link, "buy", listingId);
            if (string.Equals(operation.Status, "completed", StringComparison.OrdinalIgnoreCase))
                return ToResult(operation);

            var existingSale = _db.GetMarketplaceSaleByListing(listingId);
            if (existingSale is not null &&
                string.Equals(existingSale.BuyerOwnerType, "player", StringComparison.OrdinalIgnoreCase) &&
                existingSale.BuyerOwnerId == link.TShockUserId.ToString())
            {
                operation = CompleteOperation(operation, existingSale.SaleId);
                return ToResult(operation);
            }

            try
            {
                var sale = _marketplace.BuyNowForPlayer(
                    listingId,
                    link.TShockUserId,
                    link.TShockAccountName,
                    "web:" + operationKey);
                operation = CompleteOperation(operation, sale.SaleId);
                return ToResult(operation);
            }
            catch
            {
                MarkFailed(operation);
                throw;
            }
        }
    }

    public MarketplaceWebMutationResult Cancel(string webSubject, string listingId, string operationKey)
    {
        lock (_gate)
        {
            var link = RequireLink(webSubject);
            var operation = PrepareOperation(operationKey, link, "cancel", listingId);
            if (string.Equals(operation.Status, "completed", StringComparison.OrdinalIgnoreCase))
                return ToResult(operation);

            var listing = _db.GetMarketplaceListing(listingId)
                ?? throw Fail(operation, "Listing was not found.");
            if (string.Equals(listing.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                operation = CompleteOperation(operation, listing.ListingId);
                return ToResult(operation);
            }

            try
            {
                _marketplace.CancelListing(listingId, link.TShockUserId, _towns);
                operation = CompleteOperation(operation, listingId);
                return ToResult(operation);
            }
            catch
            {
                MarkFailed(operation);
                throw;
            }
        }
    }

    private WebAccountLink RequireLink(string webSubject)
    {
        webSubject = webSubject.Trim();
        if (webSubject.Length is < 8 or > 128)
            throw new InvalidOperationException("Invalid marketplace web subject.");
        return _db.GetWebAccountLinkBySubject(webSubject)
            ?? throw new InvalidOperationException("No linked Terraria account was found.");
    }

    private MarketplaceWebOperation PrepareOperation(
        string operationKey,
        WebAccountLink link,
        string kind,
        string listingId)
    {
        operationKey = operationKey.Trim();
        listingId = listingId.Trim();
        if (operationKey.Length is < 12 or > 96 ||
            !operationKey.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
            throw new InvalidOperationException("Invalid idempotency key.");
        if (listingId.Length is < 10 or > 64 || !listingId.StartsWith("ARK-LIST-", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid listing ID.");

        var operation = _db.GetMarketplaceWebOperation(operationKey);
        if (operation is null)
            return _db.CreateMarketplaceWebOperation(
                operationKey, link.WebSubject, link.TShockUserId, kind, listingId);

        if (!string.Equals(operation.WebSubject, link.WebSubject, StringComparison.Ordinal) ||
            operation.TShockUserId != link.TShockUserId ||
            !string.Equals(operation.Kind, kind, StringComparison.Ordinal) ||
            !string.Equals(operation.ListingId, listingId, StringComparison.Ordinal))
            throw new InvalidOperationException("That idempotency key is already bound to a different marketplace request.");

        if (string.Equals(operation.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return _db.UpdateMarketplaceWebOperation(operation.OperationKey, "failed", "pending");

        return operation;
    }

    private MarketplaceWebOperation CompleteOperation(MarketplaceWebOperation operation, string resultId)
    {
        if (string.Equals(operation.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return operation;
        return _db.UpdateMarketplaceWebOperation(operation.OperationKey, operation.Status, "completed", resultId);
    }

    private void MarkFailed(MarketplaceWebOperation operation)
    {
        try
        {
            if (string.Equals(operation.Status, "pending", StringComparison.OrdinalIgnoreCase))
                _db.UpdateMarketplaceWebOperation(operation.OperationKey, "pending", "failed");
        }
        catch
        {
            // Preserve the original marketplace exception. A pending operation can be safely recovered on retry.
        }
    }

    private InvalidOperationException Fail(MarketplaceWebOperation operation, string message)
    {
        MarkFailed(operation);
        return new InvalidOperationException(message);
    }

    private static MarketplaceWebMutationResult ToResult(MarketplaceWebOperation operation) => new(
        operation.OperationKey,
        operation.Kind,
        operation.ListingId,
        operation.Status,
        operation.ResultId);
}
