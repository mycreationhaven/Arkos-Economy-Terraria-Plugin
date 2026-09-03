using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Microsoft.Data.Sqlite;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Integrations;
using ArkoviaEconomy.Models;
using ArkoviaEconomy.Security;

static class SettlementTests
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool condition) { if (!condition) throw new Exception("Settlement regression failed at check " + (count + 1)); count++; }
        void Reject(Action act) { try { act(); } catch (InvalidOperationException) { count++; return; } throw new Exception("Expected rejection"); }
        async Task RejectAsync(Func<Task> act) { try { await act(); } catch (InvalidOperationException) { count++; return; } throw new Exception("Expected rejection"); }
        var path = Path.Combine(Path.GetTempPath(), "arkovia-settlement-" + Guid.NewGuid() + ".sqlite");
        using var connection = new SqliteConnection("Data Source=" + path); connection.Open();
        var db = new EconomyDatabase(connection); db.EnsureSchema();
        var cfg = new EconomyConfig(); cfg.EventRewards.MinimumDamage = 1;
        var economy = new EconomyService(db, () => cfg);
        var p = economy.GetOrCreatePlayer(11, "Ada"); var q = economy.GetOrCreatePlayer(12, "Ben"); var treasury = economy.GetTreasury();
        var shares = EconomyService.AllocatePool(10, new Dictionary<int,long>{{11,1},{12,1},{13,1}});
        Check(shares.Values.Sum() == 10 && shares[11] == 4);
        shares = EconomyService.AllocatePool(long.MaxValue, new Dictionary<int,long>{{11,long.MaxValue},{12,long.MaxValue}});
        Check(shares.Values.Sum() == long.MaxValue);
        Check(economy.QueueEvent("event:test", "DD2Tier1", 100, new Dictionary<int,long>{{11,1},{12,3}}));
        Reject(() => economy.SettleEvent("event:test"));
        Check(db.GetPlayerAccount(11)!.WalletAtomic == 0 && db.GetOperation("event:test")!.Status == "Queued");
        db.SetBalances(treasury.Id, 100, 0);
        Check(economy.SettleEvent("event:test"));
        Check(db.GetPlayerAccount(11)!.WalletAtomic == 25 && db.GetPlayerAccount(12)!.WalletAtomic == 75);
        Check(!economy.SettleEvent("event:test") && !economy.QueueEvent("event:test", "DD2Tier1", 100, new Dictionary<int,long>{{11,1}}));
        db.SetBalances(treasury.Id, 100, 0);
        economy.QueueEvent("event:rollback", "PirateInvasion", 100, new Dictionary<int,long>{{11,1},{12,1}});
        using (var cmd = connection.CreateCommand()) { cmd.CommandText = "CREATE TRIGGER fail_event BEFORE INSERT ON ArkoviaEconomyTransactions BEGIN SELECT RAISE(ABORT,'fault'); END"; cmd.ExecuteNonQuery(); }
        try { economy.SettleEvent("event:rollback"); throw new Exception("Expected SQL rollback"); } catch (SqliteException) { count++; }
        Check(economy.GetTreasury().WalletAtomic == 100 && db.GetPlayerAccount(11)!.WalletAtomic == 25 && db.GetOperation("event:rollback")!.Status == "Queued");
        using (var cmd = connection.CreateCommand()) { cmd.CommandText = "DROP TRIGGER fail_event"; cmd.ExecuteNonQuery(); }
        Check(economy.SettleEvent("event:rollback"));
        // Real Terraria StopInvasion invokes WinInvasionInternal inside orig.
        // Simulate that callback nesting without starting a Terraria server.
        var gameplay = new ArkoviaEconomy.Gameplay.GameplayEconomyHandler(null!, economy, () => cfg);
        var handlerType = gameplay.GetType();
        var privateFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void Field(string name, object value) => handlerType.GetField(name, privateFlags)!.SetValue(gameplay, value);
        Field("_dd2SessionActive", true); Field("_dd2Id", "event:nested-dd2"); Field("_dd2Difficulty", 1);
        Field("_dd2StartedUtc", DateTime.UtcNow.AddMinutes(-5));
        ((Dictionary<int,long>)handlerType.GetField("_dd2DamageByUserId",privateFlags)!.GetValue(gameplay)!).Add(11,100);
        var winMethod = handlerType.GetMethod("OnDd2WinInvasionInternal",privateFlags)!;
        On.Terraria.GameContent.Events.DD2Event.orig_WinInvasionInternal win = () => {};
        On.Terraria.GameContent.Events.DD2Event.orig_StopInvasion stop = won => { if(won)winMethod.Invoke(gameplay,new object[]{win}); };
        handlerType.GetMethod("OnDd2StopInvasion",privateFlags)!.Invoke(gameplay,new object[]{stop,true});
        Check(db.GetOperation("event:nested-dd2")?.Status=="Queued");
        handlerType.GetMethod("OnDd2StopInvasion",privateFlags)!.Invoke(gameplay,new object[]{stop,true});
        Check(db.Operations("event").Count(o=>o.Id=="event:nested-dd2")==1);
        Field("_dd2SessionActive", true); Field("_dd2Id", "event:lost-dd2"); Field("_dd2LossReported",true);
        ((Dictionary<int,long>)handlerType.GetField("_dd2DamageByUserId",privateFlags)!.GetValue(gameplay)!).Add(11,100);
        handlerType.GetMethod("OnDd2StopInvasion",privateFlags)!.Invoke(gameplay,new object[]{stop,false});
        Check(db.GetOperation("event:lost-dd2")==null);
        var pins = new TransactionPinService(db);
        pins.Set(11, "731946", null); pins.Verify(11, "731946");
        Check(pins.IsSet(11) && !db.GetState("pin:11")!.Contains("731946"));
        Reject(() => pins.Set(11, "888888", "111111"));
        pins.Set(11, "925831", "731946");
        for (int i = 0; i < 5; i++) Reject(() => pins.Verify(11, "000000"));
        Reject(() => new TransactionPinService(db).Verify(11, "925831"));
        Check(db.GetState("pin:11")!.Contains("LockedUntilUtc"));
        cfg.CurrencyId = "123"; cfg.BlockchainDecimals = 2;
        Check(cfg.AtomicToBlockchainExact(123000000) == 123);
        Reject(() => cfg.AtomicToBlockchainExact(1));
        Check(cfg.BlockchainToAtomicExact(123) == 123000000);
        cfg.Decimals = 0; Reject(() => cfg.BlockchainToAtomicExact(123)); cfg.Decimals = 8;
        cfg.CurrencyId = ""; cfg.BlockchainDecimals = 8;
        cfg.Transfers.Enabled = true; cfg.Transfers.ReserveAccount = "100"; cfg.Transfers.MinimumReserve = 1;
        cfg.Transfers.StarterGrant.Enabled = true; cfg.Arkovia.Enabled = false;
        Environment.SetEnvironmentVariable("ARKOVIA_SIGNER_API_KEY", "test-key-not-a-production-credential");
        db.CreatePlayerWallet(11, "300", "ARK-public-Ada", new string('1',64));
        db.CreatePlayerWallet(12, "400", "ARK-public-Ben", new string('2',64));
        var hash = new string('a',64); var txHash = hash; var chainTime = 1000; var confirmed = false; var broadcastFail = false;
        var broadcastBytes = new List<string>();
        JObject Payment(string sender, string recipient) => new()
        {
            ["sender"] = sender, ["recipient"] = recipient, ["type"] = 0, ["subtype"] = 0,
            ["amountNQT"] = "100000000", ["feeNQT"] = "1000000", ["fullHash"] = txHash,
            ["timestamp"] = 1000, ["deadline"] = 60, ["verify"] = true
        };
        var signed = Payment("100", "300");
        var deposit = Payment("300", "100"); deposit["confirmations"] = 10; deposit["block"] = "1";
        var isDeposit = true;
        var nodeHandler = new FlexibleHandler(async request =>
        {
            var raw = request.Method == HttpMethod.Post ? await request.Content!.ReadAsStringAsync() : request.RequestUri!.Query.TrimStart('?');
            var args = raw.Split('&').Select(s => s.Split('=',2)).ToDictionary(s=>WebUtility.UrlDecode(s[0]),s=>WebUtility.UrlDecode(s.Length>1?s[1]:""));
            object result = args["requestType"] switch
            {
                "getBlockchainStatus" => new { lastBlock = "1", isScanning = false, isDownloading = false },
                "getBlock" => new { timestamp = chainTime }, "getTime" => new { time = chainTime },
                "getAccount" => new { account = "100", balanceNQT = "100000000000" },
                "getBalance" => new { balanceNQT = "100000000000" },
                "getAccountCurrencies" => new { currency = "123", units = "1000000" },
                "parseTransaction" => signed,
                "getTransaction" => isDeposit ? deposit : confirmed ? Confirmed(signed) : new JObject { ["errorCode"] = 5, ["errorDescription"] = "Unknown transaction" },
                "broadcastTransaction" => new { transaction = "1" },
                _ => throw new Exception("Unexpected request " + args["requestType"])
            };
            if (args["requestType"] == "broadcastTransaction") { broadcastBytes.Add(args["transactionBytes"]); if(broadcastFail) throw new HttpRequestException("Simulated connection loss"); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(result)) };
        });
        using var node = new ArkoviaNodeClient(()=>cfg,nodeHandler);
        var signerRequests = 0;
        var signer = new FlexibleHandler(_ => { signerRequests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"transactionBytes\":\"" + new string('a',200) + "\"}") }); });
        using var transfers = new BlockchainTransferService(db,economy,node,()=>cfg,signer);
        await transfers.InitializeAsync(default);
        var before = db.GetPlayerAccount(11)!.WalletAtomic;
        Check(await transfers.DepositAsync(11,hash,default));
        Check(!await transfers.DepositAsync(11,hash,default));
        Check(db.GetPlayerAccount(11)!.WalletAtomic == before + 100000000);
        await RejectAsync(()=>transfers.DepositAsync(12,hash,default));
        deposit["confirmations"] = 0; await RejectAsync(()=>transfers.DepositAsync(11,hash,default)); deposit["confirmations"] = 10;
        deposit["phased"] = true; await RejectAsync(()=>transfers.DepositAsync(11,hash,default)); deposit.Remove("phased");
        var custom = new JObject { ["sender"]="300",["recipient"]="100",["type"]=5,["subtype"]=3,["amountNQT"]="0",["attachment"]=new JObject{["currency"]="123",["units"]="123"} };
        Check(BlockchainTransferService.ReadTransfer(custom,"123","300","100") == 123);
        Reject(()=>BlockchainTransferService.ReadTransfer(custom,"999","300","100"));
        isDeposit=false;
        var quote = await transfers.QuoteAsync(11,100000000,default);
        before=db.GetPlayerAccount(11)!.WalletAtomic;
        await RejectAsync(()=>transfers.ConfirmQuoteAsync(quote with { CreatedUtc=DateTime.UtcNow.AddMinutes(-3)},default));
        await transfers.ConfirmQuoteAsync(quote,default);
        await transfers.ConfirmQuoteAsync(quote,default);
        Check(db.GetPlayerAccount(11)!.WalletAtomic == before-100000000 && db.GetOperation(quote.Id)!.Status=="Held");
        broadcastFail=true;
        try {await transfers.TickAsync(default); throw new Exception("Expected transport failure");}catch(HttpRequestException){count++;}
        Check(db.GetPlayerAccount(11)!.WalletAtomic==before-100000000);
        broadcastFail=false;
        await transfers.TickAsync(default);
        Check(broadcastBytes.Count==2 && broadcastBytes[0]==broadcastBytes[1]);
        await RejectAsync(()=>transfers.ReleaseExpiredAsync(quote.Id,default));
        confirmed=true; await transfers.TickAsync(default); await transfers.TickAsync(default);
        Check(db.GetOperation(quote.Id)!.Status=="Confirmed" && db.GetPlayerAccount(11)!.WalletAtomic==before-100000000);
        await RejectAsync(()=>transfers.ReleaseExpiredAsync(quote.Id,default));
        Reject(() => economy.HoldWithdrawal(quote with { Id = "withdrawal:duplicate-hash" }));
        // Expired, absent payments can be reconciled once; uncertain/not-expired payments cannot.
        confirmed=false; db.SetBalances(p.Id,200000000,123);
        signed["fullHash"]=new string('b',64);
        var expired=await transfers.QuoteAsync(11,100000000,default);await transfers.ConfirmQuoteAsync(expired,default);
        chainTime=9000;await transfers.ReleaseExpiredAsync(expired.Id,default);
        Check(db.GetPlayerAccount(11)!.WalletAtomic==200000000 && db.GetPlayerAccount(11)!.BankAtomic==123);
        await RejectAsync(()=>transfers.ReleaseExpiredAsync(expired.Id,default));
        // Grants are created once and use a separate per-day cap and no gameplay debit.
        chainTime=1000; signed=Payment("100","400");signed["fullHash"]=new string('c',64);signed["amountNQT"]="1000000000";
        transfers.QueueStarterGrant(12);transfers.QueueStarterGrant(12);
        Check(db.Operations("grant",12).Count==1);
        before=db.GetPlayerAccount(12)!.WalletAtomic;
        await transfers.TickAsync(default);
        Check(db.GetOperation("grant:12")!.Status=="Held" && db.GetPlayerAccount(12)!.WalletAtomic==before);
        confirmed=true;await transfers.TickAsync(default);
        Check(db.GetOperation("grant:12")!.Status=="Confirmed");
        cfg.Transfers.StarterGrant.MaximumPerDay = 1;
        transfers.QueueStarterGrant(11);
        var attemptsBeforeCap = signerRequests;
        await transfers.TickAsync(default);
        Check(db.GetOperation("grant:11")!.Status == "Queued" && signerRequests == attemptsBeforeCap);
        // Exercise the real loopback portal: bearer session, origin, PIN setup and revocation.
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        cfg.SecurityPortal.Enabled = true; cfg.SecurityPortal.ListenUrl = $"http://127.0.0.1:{port}/";
        cfg.SecurityPortal.PublicUrl = "https://example.invalid/economy/";
        var permitted = true;
        using var portal = new SecurityPortal(db, pins, transfers, () => cfg, (_, _) => permitted);
        portal.Start();
        using var client = new HttpClient();
        var page = await client.GetAsync(cfg.SecurityPortal.ListenUrl);
        Check(page.IsSuccessStatusCode && page.Headers.Contains("Content-Security-Policy"));
        var html = await page.Content.ReadAsStringAsync();
        Check(html.Contains("Review withdrawal") && !html.Contains("NONCE_VALUE"));
        var token = portal.CreateLink(12).Split('#')[1];
        async Task<HttpStatusCode> Post(string bearer, string origin, string json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, cfg.SecurityPortal.ListenUrl + "api");
            request.Headers.Add("Origin", origin); request.Headers.Add("Authorization", "Bearer " + bearer);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request); return response.StatusCode;
        }
        Check(await Post(token, "https://wrong.invalid", "{\"action\":\"status\"}") == HttpStatusCode.BadRequest);
        Check(await Post(token, "https://example.invalid", "{\"action\":\"setPin\",\"newPin\":\"847296\"}") == HttpStatusCode.OK);
        Check(pins.IsSet(12));
        var replacement = portal.CreateLink(12).Split('#')[1];
        Check(await Post(token, "https://example.invalid", "{\"action\":\"status\"}") == HttpStatusCode.BadRequest);
        permitted = false;
        Check(await Post(replacement, "https://example.invalid", "{\"action\":\"status\"}") == HttpStatusCode.BadRequest);
        connection.Close(); SqliteConnection.ClearAllPools(); File.Delete(path);
        Console.WriteLine($"PASS: {count} settlement/security checks.");
        return count;
    }
    private static JObject Confirmed(JObject source) { var result=(JObject)source.DeepClone();result["confirmations"]=10;result["block"]="1";return result; }
    private sealed class FlexibleHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>handle(request); }
}
