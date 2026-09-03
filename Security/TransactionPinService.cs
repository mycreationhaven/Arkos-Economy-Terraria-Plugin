using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Security;

public sealed class TransactionPinService(EconomyDatabase db)
{
    private readonly object _gate = new();
    private const int Iterations = 600_000;
    private static string Key(int id) => "pin:" + id;
    private PinRecord? Read(int id) => db.GetState(Key(id)) is string raw ? JsonConvert.DeserializeObject<PinRecord>(raw) : null;
    public bool IsSet(int id) { lock (_gate) return Read(id) is not null; }
    public void Set(int id, string newPin, string? oldPin)
    {
        if (newPin.Length is < 6 or > 12 || !newPin.All(c => c is >= '0' and <= '9'))
            throw new InvalidOperationException("PIN must contain 6–12 digits.");
        lock (_gate)
        {
            if (Read(id) is not null) Verify(id, oldPin ?? "");
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(newPin, salt, Iterations, HashAlgorithmName.SHA256, 32);
            db.SetState(Key(id), JsonConvert.SerializeObject(new PinRecord(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations)));
        }
    }
    public void Verify(int id, string pin)
    {
        lock (_gate)
        {
            var stored = Read(id) ?? throw new InvalidOperationException("Set your transaction PIN first.");
            if (stored.LockedUntilUtc > DateTime.UtcNow) throw new InvalidOperationException("PIN is temporarily locked. Try again in 15 minutes.");
            var valid = pin.Length is >= 6 and <= 12 && CryptographicOperations.FixedTimeEquals(
                Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(stored.Salt), stored.Iterations, HashAlgorithmName.SHA256, 32),
                Convert.FromBase64String(stored.Hash));
            var failures = stored.LockedUntilUtc != default && stored.LockedUntilUtc <= DateTime.UtcNow ? 0 : stored.Failures;
            var next = valid ? stored with { Failures = 0, LockedUntilUtc = default }
                : stored with { Failures = failures + 1, LockedUntilUtc = failures + 1 >= 5 ? DateTime.UtcNow.AddMinutes(15) : default };
            db.SetState(Key(id), JsonConvert.SerializeObject(next));
            if (!valid) throw new InvalidOperationException("Incorrect PIN.");
        }
    }
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
