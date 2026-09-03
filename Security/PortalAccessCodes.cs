using System.Security.Cryptography;

namespace ArkoviaEconomy.Security;

/// <summary>Account-bound, one-use login codes. Never used as long-lived API credentials.</summary>
public sealed class PortalAccessCodes(Func<DateTime>? clock = null)
{
    private sealed record Entry(int UserId, byte[] Hash, DateTime Expires) { public int Failures; }
    private readonly object _gate = new();
    private readonly Dictionary<string,Entry> _codes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _attempts = new();
    private DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;
    public string Issue(int userId,string account,DateTime expires)
    {
        lock(_gate)
        {
            if(userId<=0 || string.IsNullOrWhiteSpace(account) || account.Length>128 || expires<=Now)
                throw new InvalidOperationException("Invalid portal account or expiry.");
            foreach(var key in _codes.Where(p=>p.Value.Expires<=Now || p.Value.UserId==userId).Select(p=>p.Key).ToArray())_codes.Remove(key);
            if(_codes.Count>=1000)throw new InvalidOperationException("Security portal is busy.");
            var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _codes[account]=new(userId,SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)),expires);
            return code;
        }
    }
    public (int UserId,DateTime Expires) Redeem(string account,string code)
    {
        lock(_gate)
        {
            var now=Now;
            while(_attempts.TryPeek(out var time) && time<=now.AddMinutes(-1))_attempts.Dequeue();
            // Global limiter remains effective behind a proxy; no trust in spoofable forwarding headers.
            if(_attempts.Count>=60)throw new InvalidOperationException("Too many access-code attempts. Wait one minute.");
            _attempts.Enqueue(now);
            const string error="Invalid, expired or used access code. Run /arkos security for a new code.";
            if(account.Length>128 || !_codes.TryGetValue(account,out var entry))throw new InvalidOperationException(error);
            if(entry.Expires<=now){_codes.Remove(account);throw new InvalidOperationException(error);}
            if(code.Length!=6 || !code.All(c=>c is >= '0' and <= '9') ||
                !CryptographicOperations.FixedTimeEquals(entry.Hash,SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code))))
            {
                if(++entry.Failures>=5)_codes.Remove(account);
                throw new InvalidOperationException(error);
            }
            _codes.Remove(account);
            return(entry.UserId,entry.Expires);
        }
    }
    public void Clear(){lock(_gate){_codes.Clear();_attempts.Clear();}}
}
