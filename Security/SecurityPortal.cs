using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Integrations;
using ArkoviaEconomy.Models;

namespace ArkoviaEconomy.Security;

public sealed class SecurityPortal(EconomyDatabase db, TransactionPinService pins,
    BlockchainTransferService transfers, Func<EconomyConfig> config, Func<int, string, bool> authorized) : IDisposable
{
    private sealed class Session(int userId, DateTime expires)
    {
        public int UserId = userId;
        public DateTime Expires = expires;
        public SemaphoreSlim Gate = new(1, 1);
        public EconomyOperation? Quote;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly HttpListener _listener = new();
    private readonly SemaphoreSlim _requests = new(4, 4);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    public string CreateLink(int userId)
    {
        if (!config().SecurityPortal.Enabled) throw new InvalidOperationException("Security portal is not configured.");
        foreach (var entry in _sessions.Where(e => e.Value.Expires < DateTime.UtcNow || e.Value.UserId == userId)) _sessions.TryRemove(entry.Key, out _);
        if (_sessions.Count >= 1000) throw new InvalidOperationException("Security portal is busy.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[TransactionPinService.TokenHash(token)] = new(userId, DateTime.UtcNow.AddMinutes(config().SecurityPortal.SessionMinutes));
        // Fragments are never sent in HTTP request URLs or referrers.
        return config().SecurityPortal.PublicUrl + "#" + token;
    }
    public void Start()
    {
        if (!config().SecurityPortal.Enabled) return;
        _listener.Prefixes.Add(config().SecurityPortal.ListenUrl); _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try { var context = await _listener.GetContextAsync(); _ = HandleAsync(context); }
                catch (HttpListenerException) when (_cts.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
            }
        });
    }
    private async Task HandleAsync(HttpListenerContext context)
    {
        if (!await _requests.WaitAsync(0)) { context.Response.StatusCode = 429; context.Response.Close(); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            var request = context.Request;
            if (request.HttpMethod == "GET" && request.Url!.AbsolutePath.EndsWith('/'))
            {
                var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                response.Headers["Content-Security-Policy"] = $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
                using var stream = typeof(SecurityPortal).Assembly.GetManifestResourceStream("ArkoviaEconomy.Security.portal.html")!;
                using var reader = new StreamReader(stream);
                await Send(response, (await reader.ReadToEndAsync(timeout.Token)).Replace("NONCE_VALUE", nonce), "text/html; charset=utf-8", timeout.Token);
                return;
            }
            if (request.HttpMethod != "POST" || !request.Url!.AbsolutePath.EndsWith("/api") || request.ContentLength64 is < 1 or > 4096 ||
                request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidOperationException("Invalid request.");
            var expectedOrigin = new Uri(config().SecurityPortal.PublicUrl).GetLeftPart(UriPartial.Authority);
            if (request.Headers["Origin"] != expectedOrigin) throw new InvalidOperationException("Invalid origin.");
            var bearer = request.Headers["Authorization"] ?? "";
            if (!bearer.StartsWith("Bearer ") || bearer.Length != 71 ||
                !_sessions.TryGetValue(TransactionPinService.TokenHash(bearer[7..]), out var session) || session.Expires < DateTime.UtcNow)
                throw new InvalidOperationException("Session expired. Request a new /arkos security link.");
            if (!authorized(session.UserId, Permissions.Security)) throw new InvalidOperationException("Log into Terraria with your authorized account.");
            if (!await session.Gate.WaitAsync(0)) throw new InvalidOperationException("A request is already in progress.");
            try
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = JObject.Parse(await reader.ReadToEndAsync(timeout.Token));
                var action = body.Value<string>("action");
                object result;
                switch (action)
                {
                    case "status":
                        result = new { pinSet = pins.IsSet(session.UserId), currency = config().CurrencySymbol,
                            wallet = db.GetPlayerWallet(session.UserId)?.AccountRS,
                            message = "Keep Terraria logged in. Network fees are paid by the server reserve." };
                        break;
                    case "setPin":
                        pins.Set(session.UserId, body.Value<string>("newPin") ?? "", body.Value<string>("oldPin"));
                        session.Quote = null;
                        result = new { message = "Transaction PIN saved." };
                        break;
                    case "quote":
                        if (!authorized(session.UserId, Permissions.BlockchainWithdraw)) throw new InvalidOperationException("Withdrawal permission required.");
                        pins.Verify(session.UserId, body.Value<string>("pin") ?? "");
                        if (!decimal.TryParse(body.Value<string>("amount"), System.Globalization.NumberStyles.AllowDecimalPoint,
                            System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0 ||
                            config().FromAtomic(config().ToAtomic(amount)) != amount) throw new InvalidOperationException("Enter a positive exact amount.");
                        session.Quote = await transfers.QuoteAsync(session.UserId, config().ToAtomic(amount), timeout.Token);
                        result = new { message = $"Send {config().Format(session.Quote.Atomic)} to {session.Quote.Recipient}. Network fee: {session.Quote.FeeNqt / 100000000m:0.########} ARKOS (paid by server). Quote expires in 2 minutes." };
                        break;
                    case "confirm":
                        if (!authorized(session.UserId, Permissions.BlockchainWithdraw)) throw new InvalidOperationException("Withdrawal permission required.");
                        pins.Verify(session.UserId, body.Value<string>("pin") ?? "");
                        var quote = session.Quote ?? throw new InvalidOperationException("Request a quote first.");
                        await transfers.ConfirmQuoteAsync(quote, timeout.Token);
                        result = new { message = "Withdrawal reserved and queued. Full hash: " + quote.FullHash + ". Use /arkos transfers to check confirmation." };
                        session.Quote = null;
                        break;
                    default: throw new InvalidOperationException("Unknown action.");
                }
                await Send(response, Newtonsoft.Json.JsonConvert.SerializeObject(result), "application/json", timeout.Token);
            }
            finally { session.Gate.Release(); }
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = 400;
                var message = ex is InvalidOperationException ? ex.Message : "Request could not be completed. Check /arkos transfers before retrying a withdrawal.";
                await Send(response, Newtonsoft.Json.JsonConvert.SerializeObject(new { error = message }), "application/json", timeout.Token);
            }
            catch { /* Disconnected client or shutdown; no request contents are logged. */ }
        }
        finally { response.Close(); _requests.Release(); }
    }
    private static async Task Send(HttpListenerResponse response, string content, string type, CancellationToken ct)
    { var bytes = Encoding.UTF8.GetBytes(content); response.ContentType = type; response.ContentLength64 = bytes.Length; await response.OutputStream.WriteAsync(bytes, ct); }
    public void Dispose() { _cts.Cancel(); if (_listener.IsListening) _listener.Stop(); _listener.Close(); _sessions.Clear(); }
}
