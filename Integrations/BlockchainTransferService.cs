using System.Globalization;
using System.Net.Http.Json;
using Newtonsoft.Json.Linq;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Models;
using TShockAPI;

namespace ArkoviaEconomy.Integrations;

public sealed class BlockchainTransferService(EconomyDatabase db, EconomyService economy,
    ArkoviaNodeClient node, Func<EconomyConfig> config, HttpMessageHandler? signerHandler = null) : IDisposable
{
    private readonly HttpClient _signer = signerHandler is null ? new() : new(signerHandler);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    public string ReserveId { get; private set; } = "";
    public string LastStatus { get; private set; } = "Not started";
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!config().Transfers.Enabled) return;
        await node.EnsureReadyAsync(ct);
        ReserveId = await node.ResolveAccountAsync(config().Transfers.ReserveAccount, ct);
        if (config().Arkovia.Enabled && ReserveId == await node.ResolveAccountAsync(config().Arkovia.CommunityDevelopmentAccount, ct))
            throw new InvalidOperationException("Transfer reserve must differ from the gameplay funding source to avoid double credits.");
        if (db.Operations("withdrawal").Concat(db.Operations("grant")).Any(o => o.Status == "Held" &&
            (o.Sender != ReserveId || o.CurrencyId != config().CurrencyId)))
            throw new InvalidOperationException("Resolve outstanding payments before changing the reserve or currency.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(config().Transfers.SignerApiKeyEnvironment)))
            throw new InvalidOperationException("Signer API key environment variable is missing.");
    }
    private void RequireEnabled()
    {
        if (!config().Transfers.Enabled || ReserveId.Length == 0) throw new InvalidOperationException("Blockchain transfers are not configured.");
    }
    public async Task<bool> DepositAsync(int userId, string hash, CancellationToken ct)
    {
        RequireEnabled(); hash = hash.ToLowerInvariant();
        var wallet = db.GetPlayerWallet(userId) ?? throw new InvalidOperationException("Create your linked wallet first.");
        await node.EnsureReadyAsync(ct);
        var tx = await node.TransactionAsync(hash, ct) ?? throw new InvalidOperationException("Transaction is not confirmed on the node yet.");
        if (tx.Value<string>("fullHash") != hash || tx.Value<int?>("confirmations") is not int confirmations || confirmations < config().Transfers.Confirmations || tx["block"] is null)
            throw new InvalidOperationException("Deposit is awaiting confirmations.");
        var units = ReadTransfer(tx, config().CurrencyId, wallet.AccountId, ReserveId);
        var atomic = config().BlockchainToAtomicExact(units);
        return economy.CreditBlockchainDeposit(userId, hash, atomic);
    }
    public static long ReadTransfer(JObject tx, string currency, string sender, string recipient)
    {
        if (tx.Value<string>("sender") != sender || tx.Value<string>("recipient") != recipient ||
            tx.Value<bool?>("phased") == true || tx["attachment"]?["phasingFinishHeight"] is not null ||
            tx.Value<string>("referencedTransactionFullHash") is string reference && reference.Any(c => c != '0'))
            throw new InvalidOperationException("Transaction sender, destination or conditions do not match.");
        long units;
        if (currency.Length == 0)
        {
            if (tx.Value<int?>("type") != 0 || tx.Value<int?>("subtype") != 0) throw new InvalidOperationException("Expected a native payment.");
            units = tx.Value<long?>("amountNQT") ?? 0;
        }
        else
        {
            if (tx.Value<int?>("type") != 5 || tx.Value<int?>("subtype") != 3 ||
                tx["attachment"]?.Value<string>("currency") != currency || tx.Value<long?>("amountNQT") != 0)
                throw new InvalidOperationException("Expected a transfer of the selected currency.");
            units = tx["attachment"]!.Value<long?>("units") ?? 0;
        }
        if (units <= 0) throw new InvalidOperationException("Transaction amount must be positive.");
        return units;
    }
    private async Task<EconomyOperation> PrepareAsync(int userId, long atomic, string id, string kind, CancellationToken ct)
    {
        RequireEnabled();
        var cfg = config(); var wallet = db.GetPlayerWallet(userId) ?? throw new InvalidOperationException("Create your linked wallet first.");
        var units = cfg.AtomicToBlockchainExact(atomic);
        await node.EnsureReadyAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Transfers.SignerUrl);
        request.Headers.Add("X-Arkovia-Signer-Key", Environment.GetEnvironmentVariable(cfg.Transfers.SignerApiKeyEnvironment));
        request.Content = JsonContent.Create(new { currencyId = cfg.CurrencyId, recipient = wallet.AccountId, units = units.ToString(CultureInfo.InvariantCulture), recipientPublicKey = wallet.PublicKey });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _signer.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Signer could not prepare the payment. No funds were deducted.");
        var prepared = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        var bytes = prepared.Value<string>("transactionBytes") ?? "";
        if (bytes.Length is < 100 or > 16384 || !bytes.All(Uri.IsHexDigit)) throw new InvalidOperationException("Signer returned invalid transaction bytes.");
        var parsed = await node.ParseTransactionAsync(bytes, ct);
        if (parsed.Value<bool?>("verify") != true || parsed.Value<bool?>("validate") == false || parsed["errorCode"] is not null ||
            ReadTransfer(parsed, cfg.CurrencyId, ReserveId, wallet.AccountId) != units)
            throw new InvalidOperationException("Signed transaction failed independent validation.");
        var fee = parsed.Value<long?>("feeNQT") ?? 0;
        if (fee <= 0 || fee > cfg.Transfers.MaximumNetworkFeeArkos * 100_000_000m)
            throw new InvalidOperationException("Actual network fee exceeds the configured cap.");
        var hash = parsed.Value<string>("fullHash") ?? "";
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidOperationException("Invalid signed transaction identity.");
        var op = new EconomyOperation(id, kind, userId, "Held", DateTime.UtcNow, cfg.CurrencyId, atomic, units,
            hash, bytes, wallet.AccountId, ReserveId, fee, parsed.Value<int>("timestamp"), parsed.Value<int>("deadline"));
        if (op.Deadline is < 1 or > 60) throw new InvalidOperationException("Unsupported transaction deadline.");
        return op;
    }
    public Task<EconomyOperation> QuoteAsync(int userId, long atomic, CancellationToken ct)
    {
        var cfg = config();
        if (atomic < cfg.ToAtomic(cfg.Transfers.MinimumWithdrawal) || atomic > cfg.ToAtomic(cfg.Transfers.MaximumWithdrawal))
            throw new InvalidOperationException("Amount is outside withdrawal limits.");
        return PrepareAsync(userId, atomic, "withdrawal:" + Guid.NewGuid().ToString("N"), "withdrawal", ct);
    }
    public async Task ConfirmQuoteAsync(EconomyOperation op, CancellationToken ct)
    {
        RequireEnabled();
        await _gate.WaitAsync(ct);
        try
        {
            if (db.GetOperation(op.Id) is not null) return;
            if (DateTime.UtcNow - op.CreatedUtc > TimeSpan.FromMinutes(2)) throw new InvalidOperationException("Quote expired. Request a new quote.");
            if (op.CurrencyId != config().CurrencyId || op.Sender != ReserveId || db.GetPlayerWallet(op.UserId)?.AccountId != op.Recipient)
                throw new InvalidOperationException("Withdrawal identity changed.");
            await CheckReserveAsync(op, ct);
            economy.HoldWithdrawal(op);
            // Durable signed bytes now exist BEFORE the first network submission.
            // The worker broadcasts only these exact bytes, including after a restart.
        }
        finally { _gate.Release(); }
    }
    private async Task CheckReserveAsync(EconomyOperation op, CancellationToken ct)
    {
        await node.EnsureReadyAsync(ct);
        var pending = db.Operations("withdrawal").Concat(db.Operations("grant")).Where(o => o.Status == "Held").ToArray();
        var balance = await node.GetAccountBalanceAtomicAsync(ReserveId, ct);
        var cfg = config();
        var requiredUnits = pending.Where(o => o.CurrencyId == op.CurrencyId).Sum(o => (decimal)o.Units) + op.Units +
            cfg.Transfers.MinimumReserve * (decimal)Math.Pow(10, cfg.BlockchainDecimals);
        var feeTotal = pending.Sum(o => (decimal)o.FeeNqt) + op.FeeNqt;
        if (cfg.CurrencyId.Length == 0) requiredUnits += feeTotal;
        var native = await node.QueryAsync("getBalance", ct, ("account", ReserveId));
        if (balance < requiredUnits || native.Value<long?>("balanceNQT") is not long nativeBalance || nativeBalance < feeTotal)
            throw new InvalidOperationException("Reserve cannot cover this payment, outstanding payments, fees and minimum reserve.");
    }
    public void QueueStarterGrant(int userId)
    {
        if (!config().Transfers.Enabled || !config().Transfers.StarterGrant.Enabled) return;
        economy.Locked(() =>
        {
            var id = "grant:" + userId;
            if (db.GetOperation(id) is not null) return false;
            var wallet = db.GetPlayerWallet(userId);
            if (wallet is null || DateTime.UtcNow - wallet.CreatedUtc > TimeSpan.FromMinutes(10)) return false;
            return db.Atomic(tx => { tx.Insert(new(id, "grant", userId, "Queued", DateTime.UtcNow,
                config().CurrencyId, config().ToAtomic(config().Transfers.StarterGrant.Amount))); return true; });
        });
    }
    public async Task ReleaseExpiredAsync(string id, CancellationToken ct, string actor = "TreasuryAdmin")
    {
        RequireEnabled();
        await _gate.WaitAsync(ct);
        try
        {
            var op = db.GetOperation(id) ?? throw new InvalidOperationException("Unknown operation.");
            if (op.Status != "Held" || op.Sender != ReserveId || op.CurrencyId != config().CurrencyId || op.Kind is not ("withdrawal" or "grant"))
                throw new InvalidOperationException("Operation is not an outstanding payment for this reserve.");
            await node.EnsureReadyAsync(ct);
            var now = (await node.QueryAsync("getTime", ct)).Value<int?>("time") ?? 0;
            if ((long)now <= (long)op.Timestamp + op.Deadline * 60L + 3600 || await node.TransactionAsync(op.FullHash, ct) is not null)
                throw new InvalidOperationException("Payment exists on-chain or has not expired plus the one-hour safety window. Hold retained.");
            economy.Locked(() => db.Atomic(tx =>
            {
                if (op.Kind == "withdrawal")
                {
                    var account = db.GetPlayerAccount(op.UserId) ?? throw new InvalidOperationException("Missing account.");
                    tx.Wallet(account, checked(account.WalletAtomic + op.Atomic));
                    tx.Ledger("refund:" + op.Id, null, account.Id, op.Atomic, "withdrawal_refund", op.FullHash, actor);
                }
                tx.Update(op with { Status = op.Kind == "withdrawal" ? "Refunded" : "Expired" }, "Held");
                return true;
            }));
        }
        finally { _gate.Release(); }
    }
    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));
    public async Task TickAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var op in db.Operations("event").Where(o => o.Status == "Queued"))
                try { economy.SettleEvent(op.Id); } catch (InvalidOperationException) { /* Keep the whole pool queued. */ }
            if (!config().Transfers.Enabled) { LastStatus = "Event settlement active; blockchain transfers disabled."; return; }
            await node.EnsureReadyAsync(ct);
            foreach (var candidate in db.Operations("grant").Where(o => o.Status == "Queued"))
            {
                if (!config().Transfers.StarterGrant.Enabled) break;
                var today = db.Operations("grant").Count(o => o.Status != "Queued" && o.CreatedUtc.Date == DateTime.UtcNow.Date);
                if (today >= config().Transfers.StarterGrant.MaximumPerDay) break;
                try
                {
                    var account = db.GetPlayerAccount(candidate.UserId);
                    if (account is null || account.Frozen) continue;
                    var prepared = await PrepareAsync(candidate.UserId, candidate.Atomic, candidate.Id, "grant", ct);
                    await CheckReserveAsync(prepared, ct);
                    economy.Locked(() => db.Atomic(tx => { tx.Update(prepared, "Queued");
                        tx.Insert(new("outgoing:" + prepared.FullHash, "submission", prepared.UserId, "Reserved", DateTime.UtcNow, prepared.CurrencyId, FullHash: prepared.FullHash));
                        tx.Ledger("grant:" + prepared.FullHash, null, null, prepared.Atomic, "starter_grant", prepared.FullHash, "Server"); return true; }));
                }
                catch (InvalidOperationException) { /* Retry grant when reserve/signer becomes available. */ }
            }
            foreach (var op in db.Operations("withdrawal").Concat(db.Operations("grant")).Where(o => o.Status == "Held"))
            {
                if (op.CurrencyId != config().CurrencyId || op.Sender != ReserveId) continue;
                var chain = await node.TransactionAsync(op.FullHash, ct);
                if (chain is not null && chain.Value<string>("fullHash") == op.FullHash && chain["block"] is not null &&
                    chain.Value<int?>("confirmations") >= config().Transfers.Confirmations &&
                    ReadTransfer(chain, op.CurrencyId, op.Sender, op.Recipient) == op.Units)
                {
                    economy.Locked(() => db.Atomic(tx => { tx.Update(op with { Status = "Confirmed" }, "Held");
                        tx.Ledger("confirmed:" + op.FullHash, null, null, 0, "blockchain_confirmed", op.FullHash, "ArkoviaNetwork"); return true; }));
                    continue;
                }
                try { await node.BroadcastAsync(op.SignedBytes, ct); }
                catch (ArkoviaNodeException) { /* Duplicate, expired or uncertain: NEVER refund a possibly submitted payment. */ }
            }
            LastStatus = "Settlement check completed at " + DateTime.UtcNow.ToString("u");
        }
        finally { _gate.Release(); }
    }
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { LastStatus = "Settlement pending: " + ex.GetType().Name; TShock.Log.ConsoleWarn("[ArkoviaEconomy] " + LastStatus); }
            try { await Task.Delay(TimeSpan.FromSeconds(config().Transfers.PollSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() { _cts.Cancel(); try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { } _signer.Dispose(); }
}
