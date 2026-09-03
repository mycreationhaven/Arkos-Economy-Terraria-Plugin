# Blockchain transfers, PINs, and signer setup (1.2.0 release candidate)

These features are implemented but disabled until configured. Stage-test with a small reserve before enabling player withdrawals. The plugin supports native ARKOS and the configured Monetary System currency.

## What runs where

- TShock: gameplay accounting, deposit verification, durable withdrawal holds, event settlement, and the private PIN portal.
- ArkoviaSigner: a separate process on `127.0.0.1:4892`. Holds the reserve secret in its environment, prepares signed transactions, and **never broadcasts**.
- Arkovia node: trusted local node on `127.0.0.1:4876/nxt`. Determines fees and validates/broadcasts exact signed bytes.
- HTTPS reverse proxy: exposes only the PIN portal at an operator-owned URL such as `https://your-domain.example/economy/`.

Use a dedicated on-chain reserve account. It must differ from `Arkovia.CommunityDevelopmentAccount` when automatic treasury funding is enabled, preventing the same deposit from crediting both a player and the treasury. The reserve must have selected-currency funds and native ARKOS for fees. Account IDs, node/network, and currency must agree across the plugin and signer.

## 1. Install the signer

Build with .NET 9 SDK:

```bash
dotnet publish services/ArkoviaSigner/ArkoviaSigner.csproj -c Release -o /opt/arkovia-signer
```

Alternatively, extract `release/ArkoviaSigner.zip` into that directory. Install the ASP.NET Core 9 runtime to run the framework-dependent service. It does not need access to the TShock database or player recovery files.

Set these variables in the signer's protected process environment (for example a root-managed systemd EnvironmentFile readable only by the service administrator):

| Variable | Value |
|---|---|
| `ARKOVIA_SIGNER_API_KEY` | Random shared credential, at least 32 characters |
| `ARKOVIA_RESERVE_SECRET` | Dedicated reserve's secret phrase; signer process only |
| `ARKOVIA_RESERVE_ACCOUNT_ID` | Numeric public account ID matching that secret |
| `ARKOVIA_CURRENCY_ID` | Blank for native ARKOS; otherwise selected currency ID |
| `ARKOVIA_SIGNER_NODE_URL` | Local node endpoint; default `http://127.0.0.1:4876/nxt` |
| `ARKOVIA_SIGNER_MAX_UNITS` | Positive per-payment limit in blockchain atomic units |
| `ARKOVIA_SIGNER_MAX_FEE_NQT` | Native ARKOS fee cap in atomic units; default `100000000` |

For native ARKOS, 100 ARKOS is `10000000000` units. For a two-decimal custom currency, 100 units of currency is `10000` blockchain units. Include the starter-grant size within the signer limit.

Run:

```bash
dotnet /opt/arkovia-signer/ArkoviaSigner.dll
```

The signer refuses to start if its secret derives a different public reserve ID. Its authenticated `/prepare` endpoint only constructs allowed-currency payments to a supplied public account, with amount/fee limits. It asks the node to determine the actual fee using `feeNQT=0` and `broadcast=false`. No guessed fee is charged. Requests to sign are serialized.

Set **only** `ARKOVIA_SIGNER_API_KEY` in the TShock process environment with the same value. Never give the reserve secret to TShock or put it in `config.json`. Do not expose the signer port through the proxy.

## 2. Configure the HTTPS PIN portal

Example nginx location inside your existing HTTPS virtual host:

```nginx
location /economy/ {
    proxy_pass http://127.0.0.1:4891/;
    proxy_set_header Host 127.0.0.1:4891;
    proxy_set_header Origin $http_origin;
    proxy_set_header Authorization $http_authorization;
    client_max_body_size 4k;
    proxy_read_timeout 35s;
}
```

The existing virtual host must provide a valid TLS certificate. Configure `SecurityPortal.PublicUrl` to the matching HTTPS URL with a trailing slash. The portal binds only to loopback; its HTML is embedded in the DLL. It uses no external scripts, cookies, or analytics. Do not add proxy logging of request bodies or Authorization headers.

## 3. Configure TShock

Copy the `Transfers`, `SecurityPortal`, and `EventRewards` objects from `examples/config.example.json` into the runtime configuration. Then set:

- `Transfers.Enabled=true`, `ReserveAccount` to the funded reserve, and appropriate minimum/maximum/daily limits.
- `SecurityPortal.Enabled=true` and `PublicUrl` to the HTTPS portal address.
- Keep `SignerUrl` and `ListenUrl` on loopback.
- Set confirmation depth and reserve floor. Startup and processing reject a scanning/downloading node or a chain tip older than ten minutes.

Restart TShock. Transfer and portal configuration changes require restart. Pending payments must be resolved before switching reserve or currency. Existing balances are not rescaled or reset.

Network fees are **operator-sponsored** and always paid in native ARKOS, including for custom-currency withdrawals. The gameplay Wallet is held for the withdrawal amount only; the portal shows the actual fee and who pays it. Pending payments and fees count against available reserve before another payment is accepted. This is payment-level reserve checking, not a full off-chain-liability coverage report.

## 4. Assign permissions

Players need `arkoviaeconomy.wallet` for the `/arkos` namespace, plus the relevant permissions:

| Permission | Capability |
|---|---|
| `arkoviaeconomy.security` | Open the private PIN and withdrawal portal |
| `arkoviaeconomy.blockchain.deposit` | Submit a transaction hash for verified deposit credit |
| `arkoviaeconomy.blockchain.withdraw` | Quote and confirm withdrawals through the portal |
| `arkoviaeconomy.blockchain.starter` | Eligibility for the automatic grant when creating a new wallet |
| `arkoviaeconomy.admin.treasury` | Settlement status and guarded expired-payment reconciliation under `/eco` (also requires the existing admin command permission) |

Keep starter eligibility in a trusted group. It is not granted automatically to every new registration. Eligibility plus one grant per stable TShock user ID and a server-wide daily cap limits farming; it does not establish unique real-world identity or prevent trusted users from creating alternate accounts.

## Player workflow

1. `/arkos wallet create` creates and links a public wallet, retaining the existing protected recovery flow.
2. `/arkos security` (also `/arkos pin` or `/arkos withdraw`, without arguments) opens a short-lived private link. Keep that TShock account logged in.
3. Set a 6–12 digit transaction PIN on the HTTPS page. Changing it requires the current PIN. Five incorrect attempts lock it for 15 minutes; lockouts survive restarts. Forgotten PINs require an operator-reviewed recovery procedure; there is no unauthenticated reset endpoint.
4. To deposit, transfer the selected currency **from your linked wallet** to the reserve using your wallet software. Run `/arkos deposit <64-character-full-hash>` after confirmation. If confirmations are insufficient, retry the same command later. There is no address scanning or automatic discovery of unsubmitted deposits in this release.
5. To withdraw, enter an amount and PIN on the portal, review the destination and server-paid fee, and confirm. Funds are removed from the spendable gameplay Wallet and durably held before submission. The Bank is untouched.
6. `/arkos transfers` shows recent withdrawal/grant states and public full hashes. Confirmation completes settlement; it does not deduct the Wallet a second time.

PINs are PBKDF2-SHA256 hashes with random salts and 600,000 iterations. Links use random 256-bit tokens stored server-side only as hashes. Tokens travel in URL fragments, then in Authorization headers. A new link invalidates the user's previous link; PIN setup invalidates any outstanding quote. Tokens expire after 1–10 configured minutes. Every portal request rechecks the current logged-in account and permissions.

## Starter grants

Set `Transfers.StarterGrant.Enabled=true` to send the configured amount (default 10 selected-currency units) to newly created wallets for eligible users. A candidate is persisted at wallet creation; worker processing retries if the signer or reserve is unavailable. It is not retroactive for existing wallets. The global daily cap defaults to 10 grants; queued candidates wait for a later day. Each user ID can receive only one grant, even across restarts. Grants use real reserve funds and never create gameplay balances. Recovery-service outages do not delete the protected recovery package or create another grant.

## Recovery and audit

- `/eco settlement` reports settlement status and queued event/withdrawal counts.
- A network timeout leaves a withdrawal `Held`. Restarting retries **identical signed bytes**, not a fresh payment. An outgoing full hash can be reserved only once across grants/withdrawals.
- `/eco releaseexpired <operationId>` is an explicit treasury-admin reconciliation. It only releases a held payment after the trusted node is fresh, the exact transaction is absent, and its deadline plus a one-hour grace period has passed. A withdrawal is atomically refunded once; an expired grant is marked `Expired` without a gameplay refund. A known on-chain transaction or uncertainty retains the hold.
- Do not change nodes/reserves or manually reset operation records to force a retry. Keep `ArkoviaOperations` and the ledger in backups together. Signed bytes are payment authorizations, so database access stays administrative.
- Deposits use exact sender, reserve, type/subtype, currency, amount, full-hash, and confirmation checks. Phased/conditional transfers are rejected. Replay records and credits commit together.

The trusted node remains the authority for confirmation and absence checks. Deep reorganizations beyond the configured confirmation depth are not automatically reversed; operators must reconcile those through an audited incident procedure.
