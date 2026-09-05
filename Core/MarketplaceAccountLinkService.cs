using System.Security.Cryptography;
using System.Text;
using ArkoviaEconomy.Database;

namespace ArkoviaEconomy.Core;

public sealed record MarketplaceLinkChallenge(string AccountName, string Code, DateTime ExpiresUtc);

public sealed class MarketplaceAccountLinkService
{
    private sealed record Challenge(int UserId, string AccountName, byte[] Salt, byte[] Hash, DateTime ExpiresUtc, int Attempts);

    private readonly EconomyDatabase _db;
    private readonly object _gate = new();
    private readonly Dictionary<string, Challenge> _byAccount = new(StringComparer.OrdinalIgnoreCase);

    public MarketplaceAccountLinkService(EconomyDatabase db) => _db = db;

    public MarketplaceLinkChallenge Issue(int userId, string accountName)
    {
        accountName = accountName.Trim();
        if (userId <= 0 || accountName.Length is < 1 or > 64)
            throw new InvalidOperationException("Invalid TShock account identity.");

        lock (_gate)
        {
            CleanupExpired();
            foreach (var key in _byAccount.Where(x => x.Value.UserId == userId).Select(x => x.Key).ToArray())
                _byAccount.Remove(key);

            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Hash(code, salt);
            var expires = DateTime.UtcNow.AddMinutes(10);
            _byAccount[accountName] = new Challenge(userId, accountName, salt, hash, expires, 0);
            return new MarketplaceLinkChallenge(accountName, code, expires);
        }
    }

    public WebAccountLink Redeem(string accountName, string code, string webSubject)
    {
        accountName = accountName.Trim();
        code = code.Trim();
        webSubject = webSubject.Trim();
        if (code.Length != 8 || !code.All(char.IsDigit))
            throw new InvalidOperationException("Invalid or expired link code.");

        lock (_gate)
        {
            CleanupExpired();
            if (!_byAccount.TryGetValue(accountName, out var challenge))
                throw new InvalidOperationException("Invalid or expired link code.");

            var candidate = Hash(code, challenge.Salt);
            if (!CryptographicOperations.FixedTimeEquals(candidate, challenge.Hash))
            {
                var attempts = challenge.Attempts + 1;
                if (attempts >= 5)
                    _byAccount.Remove(accountName);
                else
                    _byAccount[accountName] = challenge with { Attempts = attempts };
                throw new InvalidOperationException("Invalid or expired link code.");
            }

            var link = _db.CreateOrConfirmWebAccountLink(
                challenge.UserId, challenge.AccountName, webSubject);
            _byAccount.Remove(accountName);
            return link;
        }
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _byAccount.Where(x => x.Value.ExpiresUtc <= now).Select(x => x.Key).ToArray())
            _byAccount.Remove(key);
    }

    private static byte[] Hash(string code, byte[] salt)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var material = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, material, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, material, salt.Length, codeBytes.Length);
        return SHA256.HashData(material);
    }
}
