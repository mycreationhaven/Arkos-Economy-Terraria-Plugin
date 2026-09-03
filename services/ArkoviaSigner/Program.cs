using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

string Required(string name) => Environment.GetEnvironmentVariable(name) is string value && value.Length > 0
    ? value : throw new InvalidOperationException($"Set {name} before starting the signer.");
var key = Required("ARKOVIA_SIGNER_API_KEY");
if (key.Length < 32) throw new InvalidOperationException("Signer API key must contain at least 32 characters.");
var secret = Required("ARKOVIA_RESERVE_SECRET");
var reserve = Required("ARKOVIA_RESERVE_ACCOUNT_ID");
var currency = Environment.GetEnvironmentVariable("ARKOVIA_CURRENCY_ID") ?? "";
var nodeUrl = Environment.GetEnvironmentVariable("ARKOVIA_SIGNER_NODE_URL") ?? "http://127.0.0.1:4876/nxt";
if (!Uri.TryCreate(nodeUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
    throw new InvalidOperationException("Signer node must be local HTTP.");
var maxUnits = long.Parse(Required("ARKOVIA_SIGNER_MAX_UNITS"));
var maxFee = long.Parse(Environment.GetEnvironmentVariable("ARKOVIA_SIGNER_MAX_FEE_NQT") ?? "100000000");
if (maxUnits <= 0 || maxFee <= 0) throw new InvalidOperationException("Invalid signer limits.");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
async Task<JsonObject> Node(Dictionary<string,string> args, CancellationToken ct)
{
    using var response = await http.PostAsync(nodeUrl, new FormUrlEncodedContent(args), ct);
    response.EnsureSuccessStatusCode();
    var obj = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?.AsObject() ?? throw new InvalidOperationException();
    if (obj.ContainsKey("errorCode") || obj.ContainsKey("error")) throw new InvalidOperationException("Node rejected signer request.");
    return obj;
}
var identity = await Node(new() { ["requestType"] = "getAccountId", ["secretPhrase"] = secret }, default);
if (identity["account"]?.ToString() != reserve) throw new InvalidOperationException("Reserve secret does not match ARKOVIA_RESERVE_ACCOUNT_ID.");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:4892");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
var app = builder.Build();
var gate = new SemaphoreSlim(1, 1);
app.MapPost("/prepare", async (HttpContext context) =>
{
    var supplied = context.Request.Headers["X-Arkovia-Signer-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(key))))
        return Results.Unauthorized();
    if (!await gate.WaitAsync(0)) return Results.StatusCode(429);
    try
    {
        var input = await context.Request.ReadFromJsonAsync<PrepareRequest>();
        if (input is null || input.CurrencyId != currency || !ulong.TryParse(input.Recipient, out var recipient) || recipient == 0 ||
            !long.TryParse(input.Units, out var units) || units <= 0 || units > maxUnits ||
            input.RecipientPublicKey.Length != 64 || !input.RecipientPublicKey.All(Uri.IsHexDigit))
            return Results.BadRequest(new { error = "Invalid transfer request." });
        var parameters = new Dictionary<string,string>
        {
            ["requestType"] = currency.Length == 0 ? "sendMoney" : "transferCurrency",
            ["secretPhrase"] = secret, ["recipient"] = input.Recipient,
            ["recipientPublicKey"] = input.RecipientPublicKey,
            ["broadcast"] = "false", ["deadline"] = "60", ["feeNQT"] = "0"
        };
        if (currency.Length == 0) parameters["amountNQT"] = input.Units;
        else { parameters["currency"] = currency; parameters["units"] = input.Units; }
        // Nxt's transaction builder computes the actual minimum fee when feeNQT is zero.
        var result = await Node(parameters, context.RequestAborted);
        var tx = result["transactionJSON"];
        if (tx?["sender"]?.ToString() != reserve || tx["recipient"]?.ToString() != input.Recipient ||
            !long.TryParse(tx["feeNQT"]?.ToString(), out var fee) || fee <= 0 || fee > maxFee ||
            result["broadcasted"]?.GetValue<bool>() != false)
            return Results.BadRequest(new { error = "Transaction failed signer policy." });
        return Results.Json(new { transactionBytes = result["transactionBytes"]?.ToString() });
    }
    catch { return Results.BadRequest(new { error = "Unable to prepare transaction; nothing was broadcast." }); }
    finally { gate.Release(); }
});
app.Run();
record PrepareRequest(string CurrencyId, string Recipient, string Units, string RecipientPublicKey);
