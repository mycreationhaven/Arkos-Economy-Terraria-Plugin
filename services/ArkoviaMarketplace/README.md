# Arkovia Marketplace Web

`ArkoviaMarketplace` is the browser-facing application for the Arkovia marketplace. It is deliberately separate from the TShock plugin.

Architecture:

`browser -> HTTPS reverse proxy -> ArkoviaMarketplace -> private/loopback TShock REST -> Arkovia economy database`

The browser never receives the TShock REST token and never submits an authoritative Terraria user ID, seller ID, owner ID, balance, escrow amount, tax, or settlement result.

## Required environment variables

- `ARKOVIA_TSHOCK_REST_TOKEN` — TShock REST token with `arkoviaeconomy.api.marketplace.read`, `.link`, and `.write` permissions. Backend only.
- `ARKOVIA_MARKET_SUBJECT_SECRET` — persistent random secret of at least 32 characters used to derive an opaque stable web subject from the Terraria account name. Keep this unchanged after users begin linking.
- `ARKOVIA_TSHOCK_REST_URL` — optional; defaults to `http://127.0.0.1:7878`. Keep TShock REST private/loopback when possible.
- `ARKOVIA_MARKET_COOKIE_SECURE` — optional; defaults to `true`. Set to `false` only for local HTTP development.
- `ASPNETCORE_URLS` — recommended `http://127.0.0.1:5080` when fronted by Nginx/Caddy.

Run:

```bash
dotnet publish services/ArkoviaMarketplace/ArkoviaMarketplace.csproj -c Release -o marketplace-dist
cd marketplace-dist
ARKOVIA_TSHOCK_REST_TOKEN='...' \
ARKOVIA_MARKET_SUBJECT_SECRET='use-a-long-random-persistent-secret' \
ARKOVIA_TSHOCK_REST_URL='http://127.0.0.1:7878' \
ASPNETCORE_URLS='http://127.0.0.1:5080' \
dotnet ArkoviaMarketplace.dll
```

Expose `/marketplace` through HTTPS, for example at `https://arkovia-node1.mywire.org/marketplace`, and proxy the associated `/api/*`, `/app.js`, and `/styles.css` paths to the same service.

## Production deployment bundle

The repository includes a production-oriented deployment bundle in `deploy/arkovia-marketplace/`:

- `install.sh` publishes the service, creates a locked-down service account, writes the secret environment file with root-only permissions, installs the systemd unit, and installs the Nginx location snippet.
- `arkovia-marketplace.service` runs the app on loopback as a non-login user with systemd hardening.
- `nginx-location.conf` proxies `/marketplace`, `/api/`, `/app.js`, `/styles.css`, `/healthz`, and `/readyz` to `127.0.0.1:5080` and forwards the real client IP/protocol.
- `marketplace.env.example` documents the required runtime variables without containing real secrets.

For the intended node, run the installer from a root shell after cloning/updating the repository:

```bash
sudo bash deploy/arkovia-marketplace/install.sh
```

The installer prompts privately for the TShock REST token. If `/etc/arkovia/marketplace.env` does not yet exist, it generates the persistent marketplace subject secret automatically. Existing secrets are preserved on future deployments.

The Nginx snippet is installed to `/etc/nginx/snippets/arkovia-marketplace.conf`. Include it inside the existing HTTPS `server {}` block for `arkovia-node1.mywire.org`:

```nginx
include /etc/nginx/snippets/arkovia-marketplace.conf;
```

Then validate/reload Nginx:

```bash
sudo nginx -t && sudo systemctl reload nginx
```

Health checks:

```bash
curl -fsS http://127.0.0.1:5080/healthz
curl -fsS http://127.0.0.1:5080/readyz
curl -fsS https://arkovia-node1.mywire.org/healthz
```

`/healthz` proves the web process is alive. `/readyz` additionally verifies that the marketplace service can authenticate to the private TShock marketplace API.

## Security model

Authentication is completed with the one-time code created in Terraria by `/market link`. The backend derives the stable opaque web subject itself, redeems the code against TShock, and then issues a random server-side session cookie. The cookie is `HttpOnly`, `SameSite=Strict`, and `Secure` by default. Sessions expire after 12 hours and are intentionally invalidated by a marketplace-service restart.

State-changing browser requests require both the authenticated server-side session and a per-session CSRF token. Buy, list, and cancel requests also require an idempotency key. The browser may choose an asking price for its own listing, but TShock remains authoritative for identity, ownership, account selection, escrow, balances, taxes, and settlement.

The application applies a global IP rate limit and a tighter account-link rate limit, does not enable CORS, sends restrictive browser security headers, caps upstream response size, and suppresses normal `HttpClient` request logging so the backend TShock token is not written into standard request logs.

When Nginx is on the same host, only loopback proxy forwarding is trusted. `X-Forwarded-For` and `X-Forwarded-Proto` therefore drive the real-client IP rate limit and HTTPS/HSTS behavior without trusting arbitrary public clients to spoof those headers.

## Reverse proxy notes

Terminate TLS at the reverse proxy and keep the marketplace service and TShock REST listener bound to loopback/private interfaces. Do not expose the TShock REST port directly to the public internet. Avoid reverse-proxy logging of sensitive account-link request bodies. The browser-facing service does not need direct database access.
