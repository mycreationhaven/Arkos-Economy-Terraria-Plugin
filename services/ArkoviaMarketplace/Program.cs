using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

var settings = MarketplaceSettings.FromEnvironment();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<MarketplaceSessionStore>();
builder.Services.AddHttpClient<TShockMarketplaceClient>((sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<MarketplaceSettings>().TShockBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddFixedWindowLimiter("link", limiter =>
    {
        limiter.PermitLimit = 8;
        limiter.Window = TimeSpan.FromMinutes(5);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.IsHttps)
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});
app.UseRateLimiter();
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/marketplace"));
app.MapGet("/marketplace", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html; charset=utf-8"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "arkovia-marketplace" }));
app.MapGet("/readyz", async (TShockMarketplaceClient tshock, CancellationToken ct) =>
{
    var upstream = await tshock.GetAsync("/marketplace/api/v1/status", ct);
    return upstream.IsSuccess
        ? Results.Ok(new { status = "ready", upstream = "tshock" })
        : Results.Json(new { status = "degraded", upstream = "tshock" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/status", async (TShockMarketplaceClient tshock, CancellationToken ct) =>
    await Proxy(await tshock.GetAsync("/marketplace/api/v1/status", ct)));

app.MapGet("/api/listings", async (TShockMarketplaceClient tshock, CancellationToken ct) =>
    await Proxy(await tshock.GetAsync("/marketplace/api/v1/listings?limit=100", ct)));

app.MapGet("/api/session", (HttpContext context, MarketplaceSessionStore sessions) =>
{
    if (!sessions.TryGet(context, out var session))
        return Results.Ok(new { authenticated = false });
    return Results.Ok(new
    {
        authenticated = true,
        accountName = session.AccountName,
        csrfToken = session.CsrfToken,
        expiresUtc = session.ExpiresUtc
    });
});

app.MapPost("/api/auth/link", async (
    LinkRequest request,
    HttpContext context,
    MarketplaceSettings cfg,
    MarketplaceSessionStore sessions,
    TShockMarketplaceClient tshock,
    CancellationToken ct) =>
{
    var account = (request.Account ?? string.Empty).Trim();
    var code = (request.Code ?? string.Empty).Trim();
    if (account.Length is < 1 or > 64 || code.Length != 8 || !code.All(char.IsDigit))
        return Results.BadRequest(new { error = "Enter your Terraria account name and the 8-digit link code from /market link." });

    var subject = cfg.SubjectForAccount(account);
    var path = $"/marketplace/api/v1/link/{Uri.EscapeDataString(account)}/{Uri.EscapeDataString(code)}/{Uri.EscapeDataString(subject)}";
    var linked = await tshock.GetAsync(path, ct);
    if (!linked.IsSuccess)
        return await Proxy(linked);

    var session = sessions.Create(subject, account);
    context.Response.Cookies.Append(MarketplaceSessionStore.CookieName, session.SessionId, new CookieOptions
    {
        HttpOnly = true,
        Secure = cfg.CookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = cfg.SessionLifetime,
        IsEssential = true
    });
    return Results.Ok(new
    {
        authenticated = true,
        accountName = session.AccountName,
        csrfToken = session.CsrfToken,
        expiresUtc = session.ExpiresUtc
    });
}).RequireRateLimiting("link");

app.MapPost("/api/auth/logout", (HttpContext context, MarketplaceSettings cfg, MarketplaceSessionStore sessions) =>
{
    if (!sessions.TryGet(context, out var session))
        return Results.Ok(new { authenticated = false });
    if (!RequireCsrf(context, session))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    sessions.Remove(session.SessionId);
    context.Response.Cookies.Delete(MarketplaceSessionStore.CookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = cfg.CookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
    return Results.Ok(new { authenticated = false });
});

app.MapGet("/api/me", async (
    HttpContext context,
    MarketplaceSessionStore sessions,
    TShockMarketplaceClient tshock,
    CancellationToken ct) =>
{
    if (!sessions.TryGet(context, out var session))
        return Results.Unauthorized();
    var path = $"/marketplace/api/v1/me/{Uri.EscapeDataString(session.WebSubject)}";
    return await Proxy(await tshock.GetAsync(path, ct));
});

app.MapPost("/api/marketplace/list", async (
    ListingRequest request,
    HttpContext context,
    MarketplaceSessionStore sessions,
    TShockMarketplaceClient tshock,
    CancellationToken ct) =>
{
    if (!sessions.TryGet(context, out var session)) return Results.Unauthorized();
    if (!RequireCsrf(context, session)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!TryOperationKey(context, out var operationKey, out var operationError)) return operationError!;

    var assetId = (request.AssetId ?? string.Empty).Trim();
    var priceAtomicText = (request.PriceAtomic ?? string.Empty).Trim();
    if (!assetId.StartsWith("ARK-ASSET-", StringComparison.Ordinal) || assetId.Length > 64)
        return Results.BadRequest(new { error = "Invalid asset ID." });
    if (!long.TryParse(priceAtomicText, out var priceAtomic) || priceAtomic <= 0)
        return Results.BadRequest(new { error = "Price must be a positive atomic-unit integer." });

    var path = $"/marketplace/api/v1/mutate/list/{Uri.EscapeDataString(session.WebSubject)}/{Uri.EscapeDataString(assetId)}/{priceAtomic}/{Uri.EscapeDataString(operationKey)}";
    return await Proxy(await tshock.GetAsync(path, ct));
});

app.MapPost("/api/marketplace/buy", async (
    ListingActionRequest request,
    HttpContext context,
    MarketplaceSessionStore sessions,
    TShockMarketplaceClient tshock,
    CancellationToken ct) =>
{
    if (!sessions.TryGet(context, out var session)) return Results.Unauthorized();
    if (!RequireCsrf(context, session)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!TryOperationKey(context, out var operationKey, out var operationError)) return operationError!;
    if (!TryListingId(request.ListingId, out var listingId))
        return Results.BadRequest(new { error = "Invalid listing ID." });

    var path = $"/marketplace/api/v1/mutate/buy/{Uri.EscapeDataString(session.WebSubject)}/{Uri.EscapeDataString(listingId)}/{Uri.EscapeDataString(operationKey)}";
    return await Proxy(await tshock.GetAsync(path, ct));
});

app.MapPost("/api/marketplace/cancel", async (
    ListingActionRequest request,
    HttpContext context,
    MarketplaceSessionStore sessions,
    TShockMarketplaceClient tshock,
    CancellationToken ct) =>
{
    if (!sessions.TryGet(context, out var session)) return Results.Unauthorized();
    if (!RequireCsrf(context, session)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!TryOperationKey(context, out var operationKey, out var operationError)) return operationError!;
    if (!TryListingId(request.ListingId, out var listingId))
        return Results.BadRequest(new { error = "Invalid listing ID." });

    var path = $"/marketplace/api/v1/mutate/cancel/{Uri.EscapeDataString(session.WebSubject)}/{Uri.EscapeDataString(listingId)}/{Uri.EscapeDataString(operationKey)}";
    return await Proxy(await tshock.GetAsync(path, ct));
});

app.Run();

static bool TryListingId(string? value, out string listingId)
{
    listingId = (value ?? string.Empty).Trim();
    return listingId.StartsWith("ARK-LIST-", StringComparison.Ordinal) && listingId.Length is >= 10 and <= 64;
}

static bool TryOperationKey(HttpContext context, out string operationKey, out IResult? error)
{
    operationKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (operationKey.Length is < 12 or > 96 ||
        !operationKey.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
    {
        error = Results.BadRequest(new { error = "A valid Idempotency-Key header is required." });
        return false;
    }
    error = null;
    return true;
}

static bool RequireCsrf(HttpContext context, MarketplaceSession session)
{
    var provided = context.Request.Headers["X-CSRF-Token"].ToString();
    if (provided.Length != session.CsrfToken.Length) return false;
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided),
        Encoding.UTF8.GetBytes(session.CsrfToken));
}

static async Task<IResult> Proxy(ProxyResponse response)
{
    await Task.CompletedTask;
    return Results.Content(
        response.Body,
        "application/json; charset=utf-8",
        Encoding.UTF8,
        (int)response.StatusCode);
}

sealed record LinkRequest(string? Account, string? Code);
sealed record ListingRequest(string? AssetId, string? PriceAtomic);
sealed record ListingActionRequest(string? ListingId);
sealed record ProxyResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and <= 299;
}

sealed class MarketplaceSettings
{
    public required string TShockBaseUrl { get; init; }
    public required string TShockToken { get; init; }
    public required string SubjectSecret { get; init; }
    public bool CookieSecure { get; init; }
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(12);

    public string SubjectForAccount(string accountName)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SubjectSecret));
        var normalized = accountName.Trim().ToLowerInvariant();
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return "web:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static MarketplaceSettings FromEnvironment()
    {
        static string Required(string name)
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Required environment variable {name} is not configured.");
            return value;
        }

        var baseUrl = Environment.GetEnvironmentVariable("ARKOVIA_TSHOCK_REST_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://127.0.0.1:7878";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var subjectSecret = Required("ARKOVIA_MARKET_SUBJECT_SECRET");
        if (subjectSecret.Length < 32)
            throw new InvalidOperationException("ARKOVIA_MARKET_SUBJECT_SECRET must be at least 32 characters.");

        var secureValue = Environment.GetEnvironmentVariable("ARKOVIA_MARKET_COOKIE_SECURE")?.Trim();
        var secure = !string.Equals(secureValue, "false", StringComparison.OrdinalIgnoreCase);
        return new MarketplaceSettings
        {
            TShockBaseUrl = baseUrl,
            TShockToken = Required("ARKOVIA_TSHOCK_REST_TOKEN"),
            SubjectSecret = subjectSecret,
            CookieSecure = secure
        };
    }
}

sealed record MarketplaceSession(
    string SessionId,
    string WebSubject,
    string AccountName,
    string CsrfToken,
    DateTime ExpiresUtc);

sealed class MarketplaceSessionStore
{
    public const string CookieName = "arkovia_market_session";
    private readonly ConcurrentDictionary<string, MarketplaceSession> _sessions = new(StringComparer.Ordinal);
    private readonly MarketplaceSettings _settings;

    public MarketplaceSessionStore(MarketplaceSettings settings) => _settings = settings;

    public MarketplaceSession Create(string subject, string accountName)
    {
        CleanupExpired();
        var session = new MarketplaceSession(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            subject,
            accountName,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            DateTime.UtcNow.Add(_settings.SessionLifetime));
        _sessions[session.SessionId] = session;
        return session;
    }

    public bool TryGet(HttpContext context, out MarketplaceSession session)
    {
        session = null!;
        if (!context.Request.Cookies.TryGetValue(CookieName, out var id) || string.IsNullOrWhiteSpace(id))
            return false;
        if (!_sessions.TryGetValue(id, out var found))
            return false;
        if (found.ExpiresUtc <= DateTime.UtcNow)
        {
            _sessions.TryRemove(id, out _);
            return false;
        }
        session = found;
        return true;
    }

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _sessions)
            if (pair.Value.ExpiresUtc <= now)
                _sessions.TryRemove(pair.Key, out _);
    }
}

sealed class TShockMarketplaceClient
{
    private readonly HttpClient _http;
    private readonly MarketplaceSettings _settings;

    public TShockMarketplaceClient(HttpClient http, MarketplaceSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<ProxyResponse> GetAsync(string relativePath, CancellationToken ct)
    {
        var separator = relativePath.Contains('?') ? '&' : '?';
        var requestPath = relativePath + separator + "token=" + Uri.EscapeDataString(_settings.TShockToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 1_000_000)
            return new ProxyResponse(HttpStatusCode.BadGateway, "{\"error\":\"Upstream response was too large.\"}");
        if (string.IsNullOrWhiteSpace(body)) body = "{}";
        return new ProxyResponse(response.StatusCode, body);
    }
}
