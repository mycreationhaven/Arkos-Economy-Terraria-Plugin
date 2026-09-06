using System.Security.Cryptography;
using System.Text;
using ArkoviaEconomy.Database;

namespace ArkoviaEconomy.Core;

public sealed record MarketplaceLinkChallenge(string AccountName, string WalletAddress, string Code, DateTime ExpiresUtc);

public sealed class MarketplaceAccountLinkService
{
    private sealed record Challenge(int UserId, string AccountName, string WalletAddress, byte[] Salt, byte[] Hash, DateTime ExpiresUtc, int Attempts);
    private readonly EconomyDatabase _db;
    private readonly object _gate = new();
    private readonly Dictionary<string, Challenge> _byWallet = new(StringComparer.OrdinalIgnoreCase);
    public MarketplaceAccountLinkService(EconomyDatabase db) => _db = db;

    public MarketplaceLinkChallenge Issue(int userId, string accountName)
    {
        accountName = accountName.Trim();
        if (userId <= 0 || accountName.Length is < 1 or > 64) throw new InvalidOperationException("Invalid TShock account identity.");
        var wallet = _db.GetPlayerWallet(userId) ?? throw new InvalidOperationException("Create your ARKOS wallet first with /arkos wallet create.");
        var walletAddress = wallet.AccountRS.Trim();
        lock (_gate)
        {
            CleanupExpired();
            foreach (var key in _byWallet.Where(x => x.Value.UserId == userId).Select(x => x.Key).ToArray()) _byWallet.Remove(key);
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var salt = RandomNumberGenerator.GetBytes(16); var hash = Hash(code, salt); var expires = DateTime.UtcNow.AddMinutes(5);
            _byWallet[walletAddress] = new Challenge(userId, accountName, walletAddress, salt, hash, expires, 0);
            return new MarketplaceLinkChallenge(accountName, walletAddress, code, expires);
        }
    }

    public WebAccountLink Redeem(string walletAddress, string code, string requestedWebSubject)
    {
        walletAddress = walletAddress.Trim(); code = code.Trim(); requestedWebSubject = requestedWebSubject.Trim();
        if (walletAddress.Length is < 3 or > 64 || code.Length != 6 || !code.All(char.IsDigit)) throw new InvalidOperationException("Invalid or expired authentication code.");
        lock (_gate)
        {
            CleanupExpired();
            if (!_byWallet.TryGetValue(walletAddress, out var challenge)) throw new InvalidOperationException("Invalid or expired authentication code.");
            var wallet = _db.GetPlayerWalletByAddress(walletAddress);
            if (wallet is null || wallet.TShockUserId != challenge.UserId) throw new InvalidOperationException("Invalid or expired authentication code.");
            var candidate = Hash(code, challenge.Salt);
            if (!CryptographicOperations.FixedTimeEquals(candidate, challenge.Hash))
            {
                var attempts = challenge.Attempts + 1;
                if (attempts >= 5) _byWallet.Remove(walletAddress); else _byWallet[walletAddress] = challenge with { Attempts = attempts };
                throw new InvalidOperationException("Invalid or expired authentication code.");
            }

            // Existing players are migrated to the wallet-derived web subject. Marketplace
            // ownership remains keyed to the authoritative TShock user, so history/assets survive.
            var existing = _db.GetWebAccountLinkByUser(challenge.UserId);
            var link = existing is null
                ? _db.CreateOrConfirmWebAccountLink(challenge.UserId, challenge.AccountName, requestedWebSubject)
                : _db.RebindWebAccountLinkSubject(challenge.UserId, requestedWebSubject);
            _byWallet.Remove(walletAddress);
            return link;
        }
    }

    private void CleanupExpired(){var now=DateTime.UtcNow;foreach(var key in _byWallet.Where(x=>x.Value.ExpiresUtc<=now).Select(x=>x.Key).ToArray())_byWallet.Remove(key);}
    private static byte[] Hash(string code,byte[] salt){var codeBytes=Encoding.UTF8.GetBytes(code);var material=new byte[salt.Length+codeBytes.Length];Buffer.BlockCopy(salt,0,material,0,salt.Length);Buffer.BlockCopy(codeBytes,0,material,salt.Length,codeBytes.Length);return SHA256.HashData(material);}
}
