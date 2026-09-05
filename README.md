# 🌎 Arkovia Economy for Terraria / TShock

> A treasury-backed Terraria economy and player marketplace for TShock, with Wallet/Bank balances, towns and property, live web marketplace inventory selling, stock holdings, voting rewards, progression, and optional Arkovia blockchain integration.

**Current release line: `v1.5.0-rc.1`**  
**Plugin version reported by TShock: `1.5.0`**  
**Live Arkovia marketplace:** `https://arkovia-node1.mywire.org/marketplace`  
The shorter `/market` URL redirects to the same marketplace.

Arkovia Economy is an open-source TShock plugin for Terraria. It keeps fast gameplay transactions in an internal integer ledger while allowing selected workflows—wallet creation, deposits/withdrawals, marketplace settlement, towns, property, and future company systems—to connect safely to the broader Arkovia platform.

The project is intentionally server-authoritative. Browsers are never trusted to decide balances, ownership, inventory contents, prices already committed to an order, permissions, or settlement completion.

## What v1.5 adds

Version 1.5 expands the plugin from a gameplay economy into a broader server platform:

- live website marketplace with linked Terraria accounts;
- six-digit `/market link` codes that expire after five minutes;
- live in-game inventory visibility on the website while the player is online;
- selecting an inventory slot, quantity, and total ARKOS price from the website;
- removal of listed items from the live Terraria inventory into marketplace escrow;
- purchased/returned item claiming back into Terraria;
- player marketplace profiles with sellable assets, listings, purchases, inventory, and stock holdings;
- a public scrolling stock marquee on the marketplace;
- clickable stock details and website stock purchases;
- `/stocks` and `/stock` commands for market and portfolio views;
- town creation, membership, treasury, claims, governance, property, and town-property marketplace support;
- secure website mutations with linked identity, CSRF protection, rate limiting, and idempotency keys;
- atomic marketplace escrow and settlement for supported transferable assets;
- restored `/market` → `/marketplace` web alias;
- corrected TShock REST route binding for marketplace account linking and mutations.

The stock module in this release is a **primary-offering foundation**. It supports issued shares, current prices, available shares, holdings, and purchases from the issuer. A full player-to-player order book with bids, asks, price-time priority, and secondary trading remains a later phase.

---

## Feature status

| Area | Status |
|---|---|
| TShock account-backed economy | ✅ Production foundation |
| Integer/atomic Wallet + Bank accounting | ✅ |
| Atomic settlement + immutable ledger | ✅ |
| Player payments | ✅ |
| Treasury-backed gameplay rewards | ✅ |
| NPC rewards, death penalties, PvP economy | ✅ |
| Paid ranks, quests, jobs | ✅ |
| Vote rewards | ✅ |
| Player-created Arkovia wallets | ✅ |
| Blockchain deposits/withdrawals | ✅ Available when configured |
| Transaction PIN + secure portal | ✅ Available when configured |
| Towns and membership | ✅ |
| Town treasury | ✅ |
| TShock region/property claims | ✅ |
| Town governance | ✅ |
| Town-property marketplace settlement | ✅ |
| Website account linking | ✅ |
| Website player profile | ✅ |
| Live online inventory view | ✅ |
| Website inventory-item listing | ✅ |
| Item escrow + claim delivery | ✅ Initial release |
| Generic asset marketplace | ✅ |
| Stock quotes + holdings | ✅ Initial release |
| Website stock marquee | ✅ |
| Primary stock purchases | ✅ |
| Secondary stock order book | 🚧 Planned |
| Rentals | 🚧 Planned |
| Companies/businesses | 🚧 Planned |
| Smart-region automation | 🚧 Planned |
| Optional crossplay integration | 🚧 Planned |

---

# Architecture

```text
Terraria / TShock
  ├─ player identity
  ├─ live inventory
  ├─ regions / world ownership
  └─ permissions
          │
          ▼
Arkovia Economy Plugin
  ├─ Wallet / Bank ledger
  ├─ towns / property
  ├─ marketplace assets
  ├─ item escrow
  ├─ stock holdings
  └─ secure TShock REST routes
          │
          ▼
Arkovia Marketplace Web Service
  ├─ HTTPS browser sessions
  ├─ CSRF protection
  ├─ rate limiting
  ├─ account-link sessions
  └─ server-side TShock REST token
          │
          ▼
Browser
```

The browser does **not** receive the TShock REST token and does not connect directly to the TShock database.

## Core authority rules

1. TShock is authoritative for the logged-in Terraria identity, live character inventory, permissions, and world/region state.
2. The Arkovia plugin/backend is authoritative for balances, marketplace records, escrow, ownership, shares, and settlement state.
3. The browser can request an action, but it cannot declare that an action succeeded.
4. Ownership is not transferred until settlement succeeds.
5. All supported monetary movements are recorded through the economy ledger.
6. External mutations require linked identity, authorization, CSRF/session protection where applicable, idempotency, and auditability.

---

# Marketplace

The production marketplace is served at:

```text
https://arkovia-node1.mywire.org/marketplace
```

`https://arkovia-node1.mywire.org/market` redirects there for convenience.

## Link a Terraria account

In Terraria:

```text
/market link
```

The plugin returns a cryptographically generated **six-digit, single-use code**. The code expires after **five minutes** and allows at most five failed guesses.

Enter the Terraria account name and code on the marketplace. The website derives a stable opaque subject from the normalized account name using a server-side HMAC secret and keeps the TShock REST credential entirely server-side.

## Player profile

After linking, the marketplace profile can show:

- linked Terraria account;
- live online inventory;
- stock holdings and current market value;
- other transferable assets;
- active and historical marketplace listings;
- purchase history;
- items waiting to be claimed back into Terraria.

## Sell directly from Terraria inventory

The player must currently be online so the website can read the authoritative live character inventory.

Flow:

```text
1. Link the account on the marketplace.
2. Stay logged into Terraria.
3. Open the In-game inventory section on the website.
4. Choose an item.
5. Choose the quantity.
6. Enter the total ARKOS listing price.
7. Confirm the listing.
8. The server verifies the slot and stack again.
9. The item quantity is removed from Terraria and represented in marketplace escrow.
10. The listing becomes available for purchase.
```

Favorited items cannot be listed until they are unfavorited in Terraria.

The website never trusts a browser-provided item name or item ID as proof of ownership. The listing route resolves the linked TShock account, reads the live slot again, checks the stack and favorite state, creates an item-backed asset/escrow record, and then creates the marketplace listing.

## Claim purchased or returned items

Players can use the website **Claim to Terraria** action while online or run:

```text
/claimitems
```

Claimable item assets are delivered to the linked Terraria account and then marked consumed/delivered by the marketplace system.

## Generic marketplace assets

The marketplace also supports stable IDs in the form:

```text
ARK-ASSET-<GUID>
```

Supported asset workflows include ordinary player assets and town-backed property. Property settlement additionally updates the associated TShock region ownership/ACL state.

Marketplace settlement uses explicit listing, reservation, escrow, completion, cancellation, expiry, and sale records. Browser values alone never finalize a transaction.

---

# Stocks / Arkovia Exchange

The marketplace includes an **ARKOVIA EXCHANGE** scrolling marquee. Each stock is clickable and displays its ticker, current price, and available shares.

Linked players can buy available primary-offering shares from the website and see their holdings on their profile.

In Terraria:

```text
/stocks
/stocks market
/stocks mine
/stocks portfolio
/stocks buy <ticker> <shares>
```

Alias:

```text
/stock
```

Administrator stock setup:

```text
/stockadmin create <ticker> <name> <price> <shares>
/stockadmin price <ticker> <price>
```

A purchase atomically:

- verifies the current stock quote and available shares;
- verifies the buyer Wallet balance;
- debits the buyer Wallet;
- credits the issuer economy account;
- reduces available shares;
- increases the buyer holding;
- writes an economy ledger row.

### Current limitation

This is not yet a full exchange matching engine. Shareholders cannot yet place their own bid/ask orders or sell holdings to one another through an order book. That phase will add locked shares/funds, price-time priority, self-trade protections, and atomic secondary settlement.

---

# Towns and property

Towns use stable town/asset records plus TShock regions.

Common commands:

```text
/town create <name>
/town info
/town invite <player>
/town accept <town>
/town leave
/town balance
/town deposit <amount>
/town withdraw <amount>
/town claim <region>
/town unclaim <region>
/town promote <player>
/town demote <player>
/town kick <player>
/town transfer <player>
/property info
```

Marketplace property commands include:

```text
/market listings
/market info <listingId>
/market sellproperty <region> <price>
/market buy <listingId>
/market cancel <listingId>
```

Town-property sale settlement updates the economy escrow, seller town treasury, configured marketplace tax, property record, asset owner, transfer audit, marketplace sale, and TShock region ownership as one guarded workflow.

---

# Economy model

Arkovia Economy separates three concepts.

## Gameplay Wallet

Spendable internal currency used for payments, marketplace purchases, fees, rewards, stock purchases, and other gameplay systems.

## Gameplay Bank

Protected internal savings. Normal death/PvP losses do not drain the Bank.

```text
/bank balance
/bank deposit <amount>
/bank withdraw <amount>
```

## Arkovia blockchain wallet

A real Arkovia account linked to the stable TShock user ID. This is separate from the gameplay Wallet and Bank.

```text
/arkos balance
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
```

Never type an Arkovia recovery phrase, private key, or transaction PIN into Terraria chat.

---

# Atomic accounting

Internal balances are integer atomic units rather than floating point.

For native ARKOS:

```text
1 ARKOS = 100,000,000 atomic units
```

The settlement layer uses optimistic before-value checks and SQL transactions so concurrent balance changes abort instead of silently creating partial transfers.

Marketplace and stock monetary operations are designed around the same integer ledger model.

---

# Gameplay economy

The plugin includes:

- configurable NPC rewards;
- treasury solvency enforcement;
- normal death Wallet deductions;
- protected Bank balances;
- PvP redistribution;
- floating positive/negative Terraria combat text;
- configurable event pools;
- progression ranks, quests, and jobs.

Frequent gameplay transactions remain off-chain. Killing an NPC does not create an Arkovia blockchain transaction.

---

# Voting rewards

The plugin avoids TShock's built-in `/vote` poll command collision by using:

```text
/arkvote links
/arkvote claim [provider]
/arkvote status
/arkvote debug
/arkvote tserverweb [captcha-answer]
```

Alias:

```text
/voterewards
```

Supported integrations include Terraria-Servers.com and TServerWeb, with configurable treasury-backed currency, item, and temporary-group rewards.

See [`docs/VOTING.md`](docs/VOTING.md).

---

# Blockchain settlement

Configured deployments can support:

```text
/arkos deposit
/arkos deposit <fullHash>
/arkos security
/arkos pin
/arkos withdraw
/arkos transfers
```

The signing service is intentionally separate from the TShock plugin. Private signing credentials and API keys must remain in protected server-side environment/configuration storage and must never be committed.

See [`docs/BLOCKCHAIN_SETUP.md`](docs/BLOCKCHAIN_SETUP.md) and [`docs/SECURITY.md`](docs/SECURITY.md).

---

# Installation

## Plugin

Requirements:

- Terraria/TShock compatible with the target release;
- .NET 9 runtime for the current TShock deployment;
- SQLite or supported TShock database provider.

Install:

```text
1. Download ArkoviaEconomy.dll from the latest GitHub Release.
2. Place it in ServerPlugins/.
3. Restart TShock.
4. Review the generated ArkoviaEconomy configuration.
5. Configure permissions for player/staff groups.
```

Do not hot-reload a release that adds/removes marketplace REST routes; a full TShock restart is the safer deployment path.

## Marketplace web service

The web service lives in:

```text
services/ArkoviaMarketplace/
```

Production deployments should bind it to loopback behind HTTPS Nginx or another trusted reverse proxy.

Required environment values:

```text
ARKOVIA_TSHOCK_REST_TOKEN
ARKOVIA_MARKET_SUBJECT_SECRET
```

Common optional values:

```text
ARKOVIA_TSHOCK_REST_URL=http://127.0.0.1:7878
ARKOVIA_MARKET_COOKIE_SECURE=true
ASPNETCORE_URLS=http://127.0.0.1:5080
```

The subject secret must be persistent. Changing it after accounts have linked would change derived web subjects and break existing links.

The TShock REST token should belong to a dedicated least-privilege group with only the marketplace API permissions it needs.

---

# Important permissions

Core:

```text
arkoviaeconomy.use
arkoviaeconomy.pay
arkoviaeconomy.bank
arkoviaeconomy.shop
arkoviaeconomy.market
arkoviaeconomy.jobs
arkoviaeconomy.vote
arkoviaeconomy.wallet
```

Towns/property:

```text
arkoviaeconomy.town
arkoviaeconomy.town.create
arkoviaeconomy.town.manage
arkoviaeconomy.town.claim
arkoviaeconomy.town.bank
arkoviaeconomy.property
arkoviaeconomy.admin.town
```

Marketplace REST service:

```text
arkoviaeconomy.api.marketplace.read
arkoviaeconomy.api.marketplace.link
arkoviaeconomy.api.marketplace.write
```

Administration:

```text
arkoviaeconomy.admin
arkoviaeconomy.admin.adjust
arkoviaeconomy.admin.treasury
arkoviaeconomy.admin.config
arkoviaeconomy.admin.audit
arkoviaeconomy.admin.vote
```

See [`docs/COMMANDS.md`](docs/COMMANDS.md) for the command reference.

---

# Security notes

Production invariants for this project:

- no balance mutation without an accompanying ledger record;
- no externally requested mutation without authenticated identity and server-side authorization;
- no settlement based solely on browser-supplied values;
- no ownership transfer before settlement completion;
- no plaintext recovery secrets/private keys/PINs in logs, URLs, normal config, or ordinary gameplay records;
- marketplace browser sessions use HttpOnly cookies, SameSite=Strict, CSRF tokens, rate limiting, and short server-side trust paths;
- TShock REST should remain private/firewalled and should not be exposed directly to the public internet;
- command aliases should not shadow unrelated TShock commands by default.

Review [`SECURITY.md`](SECURITY.md) and [`docs/SECURITY.md`](docs/SECURITY.md) before public deployment.

---

# Testing

The repository has GitHub Actions build/regression checks for the plugin, signer, and marketplace service.

Typical local validation:

```bash
dotnet build -c Release
dotnet run --project tests/ArkoviaEconomy.Tests.csproj -c Release
node tests/portal_ui_smoke.js
node tests/marketplace_web_smoke.js
dotnet build services/ArkoviaMarketplace/ArkoviaMarketplace.csproj -c Release
```

The current v1.5 implementation increased the main regression suite to **349 checks** in addition to the settlement/security test group and web smoke tests.

Live Terraria hooks, inventory delivery, real multiplayer behavior, MySQL-specific behavior, reverse-proxy configuration, and external blockchain/provider integrations still require staging/production validation beyond unit/regression tests.

---

# Release artifacts

The authoritative downloadable plugin is published on the repository's **GitHub Releases** page as:

```text
ArkoviaEconomy.dll
```

Starting with `v1.5.0-rc.1`, the repository release workflow builds the DLL from the tagged commit, generates a SHA-256 checksum, and publishes both to GitHub Releases. This avoids keeping a stale compiled DLL in source control.

The `release/` directory contains release metadata and operator notes; GitHub Releases is the source of truth for the compiled plugin binary.

---

# Roadmap

The platform roadmap is documented in [`docs/PLATFORM_ROADMAP.md`](docs/PLATFORM_ROADMAP.md).

Near-term work includes:

- hardening item escrow/reconciliation around process crashes;
- richer marketplace item presentation and filters;
- player-to-player secondary stock exchange/order book;
- companies/businesses and company treasuries;
- rentals and lease state;
- smart-region automation;
- staff quality-of-life tooling;
- optional crossplay integration kept separate from the economy authority model.

---

# Documentation

- [`docs/COMMANDS.md`](docs/COMMANDS.md) — commands and permissions
- [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) — economy configuration
- [`docs/BLOCKCHAIN_SETUP.md`](docs/BLOCKCHAIN_SETUP.md) — deposits, withdrawals, signer, PIN setup
- [`docs/VOTING.md`](docs/VOTING.md) — voting providers/rewards
- [`docs/PROGRESSION.md`](docs/PROGRESSION.md) — ranks, quests, jobs
- [`docs/EVENT_REWARDS.md`](docs/EVENT_REWARDS.md) — event settlement
- [`docs/PLATFORM_ROADMAP.md`](docs/PLATFORM_ROADMAP.md) — towns, marketplace, companies, exchange roadmap
- [`docs/SECURITY.md`](docs/SECURITY.md) — security architecture
- [`CHANGELOG.md`](CHANGELOG.md) — release history
- [`VALIDATION.md`](VALIDATION.md) — validation notes

---

## Project direction

Arkovia Economy is evolving into a persistent Terraria economy/platform where players can earn, save, trade, own property, build towns and businesses, participate in player-created companies, and manage assets from both Terraria and the web—while keeping authority and settlement on trusted server-side systems rather than in the browser.
