# 🌎 Arkovia Economy for Terraria / TShock

Arkovia Economy is a server-authoritative economy and community platform for Terraria/TShock. It combines Wallet and Bank balances, towns and property, a live web marketplace, item escrow, stock holdings, vote rewards, progression, optional Arkovia blockchain settlement, and a companion crossplay bridge for compatible PC/mobile Terraria versions.

**Current release line:** `v1.5.0-rc.1`  
**Main economy plugin:** `ArkoviaEconomy.dll`  
**Crossplay companion:** `ArkoviaCrossplay.dll`  
**Target server stack:** TShock 6.1 / .NET 9 / Terraria 1.4.5.x  
**Live marketplace:** `https://arkovia-node1.mywire.org/marketplace`  
**Short marketplace URL:** `https://arkovia-node1.mywire.org/market`

> The project is intentionally server-authoritative. Browsers never decide balances, inventory ownership, property ownership, permissions, committed prices, or settlement completion.

---

## Current feature status

| Area | Status |
|---|---|
| TShock account-backed economy | ✅ |
| Integer/atomic Wallet + Bank accounting | ✅ |
| Atomic settlement + immutable ledger | ✅ |
| Player payments and treasury flows | ✅ |
| NPC rewards, death penalties and PvP economy | ✅ |
| Paid ranks, quests and jobs | ✅ |
| Vote rewards | ✅ |
| Arkovia wallet creation | ✅ |
| Blockchain deposits/withdrawals | ✅ when configured |
| Transaction PIN / secure portal | ✅ when configured |
| Towns, membership and governance | ✅ |
| Town treasury | ✅ |
| Region/property claims | ✅ |
| Property marketplace settlement | ✅ |
| Website account linking | ✅ |
| Website player profile | ✅ |
| Live online Terraria inventory | ✅ |
| Website inventory-item listing | ✅ |
| Item escrow and claim delivery | ✅ initial release |
| Generic transferable assets | ✅ |
| Stock quotes and holdings | ✅ initial release |
| Website stock marquee | ✅ |
| Primary stock purchases | ✅ |
| Terraria item artwork proxy | ✅ |
| PC gameplay | ✅ |
| Mobile 1.4.5.x compatibility bridge | ✅ deployed; real-device validation ongoing |
| Xbox / PlayStation / Switch direct support | ⏳ not provided by the bridge alone |
| Secondary stock order book | 🚧 planned |
| Rentals | 🚧 planned |
| Companies/businesses | 🚧 planned |
| Smart-region automation | 🚧 planned |

---

# What v1.5 adds

Version 1.5 expands Arkovia Economy from a gameplay economy into a broader server platform.

- Production Arkovia Marketplace web application.
- Linked Terraria accounts using six-digit, five-minute, single-use sign-in codes.
- Fresh re-authentication codes for accounts that are already permanently linked.
- Live in-game inventory visibility while the player is online.
- Website listing of Terraria items by live slot, quantity and total ARKOS price.
- Removal of listed quantity from the live character inventory into marketplace escrow.
- Purchased/returned item claiming back into Terraria.
- Player marketplace profiles with listings, purchases, inventory and stock holdings.
- Scrolling clickable ARKOVIA EXCHANGE stock marquee.
- Stock details and primary share purchases from the website.
- `/stocks` and `/stock` commands.
- Town creation, membership, treasury, claims, governance and property.
- Town-property marketplace settlement with TShock region ownership updates.
- Secure marketplace writes using linked identity, CSRF protection, rate limits and idempotency.
- Atomic marketplace reservation/escrow/settlement for supported assets.
- Restored `/market` → `/marketplace` web alias.
- Corrected TShock REST route binding for marketplace account linking and mutations.
- Same-origin cached Terraria item-art proxy for more reliable inventory artwork.
- Companion **Arkovia Crossplay Bridge** for approved Terraria 1.4.5.x PC/mobile protocol versions.

The stock module is currently a **primary-offering foundation**. It supports issued shares, current price, available shares, holdings and purchases from the issuer. A true player-to-player order book with bids, asks, locked shares/funds and price-time priority is a later phase.

---

# Architecture

```text
Terraria / TShock
  ├─ player identity
  ├─ live inventory
  ├─ regions / world ownership
  ├─ permissions
  └─ connection protocol
          │
          ├──────────────► Arkovia Crossplay Bridge
          │                 └─ approved PC/mobile 1.4.5.x handshake compatibility
          │
          ▼
Arkovia Economy Plugin
  ├─ Wallet / Bank ledger
  ├─ treasury
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
  ├─ account linking
  ├─ item artwork proxy/cache
  └─ server-side TShock REST token
          │
          ▼
Browser
```

Core authority rules:

1. TShock is authoritative for Terraria identity, live inventory, permissions and world/region state.
2. The Arkovia backend is authoritative for balances, marketplace records, escrow, asset ownership and shares.
3. Browsers may request actions but cannot declare success.
4. Ownership is not transferred until settlement succeeds.
5. Supported monetary movements use the economy ledger.
6. External mutations require linked identity, authorization, idempotency and auditability.
7. TShock REST credentials and signing secrets stay server-side.

---

# Crossplay

Arkovia now includes a separate companion plugin in:

```text
crossplay/ArkoviaCrossplay/
```

Compiled plugin name:

```text
ArkoviaCrossplay.dll
```

The bridge is designed for **TShock 6.1 / .NET 9 / Terraria 1.4.5.x** and runs alongside `ArkoviaEconomy.dll`.

## What it does

Terraria sends a protocol string such as `Terraria319` during the initial connection request. Normally, TShock/Terraria rejects a client whose protocol does not match the server exactly.

Arkovia Crossplay runs a high-priority `NetGetData` hook and, for an explicitly approved protocol, rewrites the connection handshake to the server's current protocol before normal version validation continues.

The live server currently reports:

```text
Terraria v1.4.5.8 / Terraria319
```

The default compatibility allow-list includes:

```text
Terraria311
Terraria312
Terraria313
Terraria314
Terraria315
Terraria316
Terraria317
Terraria318
Terraria319
```

Unknown protocols are **not** automatically accepted. The bridge fails closed and leaves normal server validation in place.

## Crossplay commands

Permission:

```text
arkovia.crossplay.admin
```

Commands:

```text
/arcrossplay info
/arcrossplay versions
/arcrossplay verbose
/arcrossplay reload
```

Alias:

```text
/acp
```

`/arcrossplay verbose` is especially useful when testing a mobile client because it records the exact protocol string presented during connection.

## Platform support

### PC

Supported normally on the server's compatible Terraria version.

### Mobile

The 1.4.5.x handshake compatibility bridge is deployed and active. Compatible mobile clients in the approved protocol range are expected to pass the version mismatch check. Real-device end-to-end validation should still verify movement, inventory, combat, chests, SSC, reconnect behavior and Arkovia systems before broad public claims are made.

### Xbox, PlayStation and Nintendo Switch

The current bridge **does not by itself provide console connectivity**. Consoles have platform networking, discovery and transport restrictions beyond the Terraria protocol-version handshake. The plugin does not create a console server browser, custom-IP join feature or bypass Xbox/PlayStation/Nintendo networking requirements.

The architecture is intentionally ready for console players once Terraria/platform networking exposes them through a compatible multiplayer transport.

## Important limitation

The current Arkovia bridge is primarily a **handshake compatibility bridge**, not a universal packet translation engine. It should only approve versions that have been tested to share compatible packet behavior.

---

# Marketplace

Production marketplace:

```text
https://arkovia-node1.mywire.org/marketplace
```

Convenience redirect:

```text
https://arkovia-node1.mywire.org/market
```

## Link a Terraria account

In Terraria:

```text
/market link
```

The plugin creates a cryptographically generated six-digit code that expires after five minutes and limits failed guesses. Enter the Terraria account name and code on the marketplace.

If an account is already linked but the browser session is gone, `/market link` issues a fresh authentication code without changing the durable account link.

## Live inventory selling

While online in Terraria, a linked player can:

```text
1. Open the marketplace.
2. View the live in-game inventory.
3. Select an item.
4. Choose quantity.
5. Enter the total listing price.
6. Confirm the listing.
7. Let the server re-read and validate the actual slot and stack.
8. Move the quantity from Terraria into marketplace escrow.
```

Favorited items cannot be listed until unfavorited in Terraria.

The browser's item name or item ID is never accepted as proof of ownership. TShock's live inventory state remains authoritative.

## Claim items

Players can use the website's claim action while online or run:

```text
/claimitems
```

Purchased or returned item assets are delivered to Terraria and then marked delivered/consumed in marketplace state.

## Terraria item artwork

Inventory cards use a same-origin endpoint:

```text
/api/item-image/{itemId}?name=<display-name>
```

The marketplace server fetches artwork from a fixed Terraria wiki host, validates PNG responses and caches successful images in memory. Live item identity, slot and quantity still come from TShock; external artwork is presentation only.

---

# Stocks / Arkovia Exchange

Commands:

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

Administrator setup:

```text
/stockadmin create <ticker> <name> <price> <shares>
/stockadmin price <ticker> <price>
```

A primary share purchase verifies the quote and available shares, verifies the buyer Wallet, debits the buyer, credits the issuer account, reduces available shares, increases the player's holding and writes an economy ledger row.

Secondary bid/ask matching is not yet implemented.

---

# Towns and property

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

Marketplace property commands:

```text
/market listings
/market info <listingId>
/market sellproperty <region> <price>
/market buy <listingId>
/market cancel <listingId>
```

Property sale settlement guards the economy escrow, seller town treasury, configured sales tax, property record, asset owner, transfer audit, marketplace sale and associated TShock region ownership/ACL state.

---

# Economy model

## Gameplay Wallet

Spendable internal currency used for payments, marketplace purchases, fees, rewards and stock purchases.

## Gameplay Bank

Protected internal savings.

```text
/bank balance
/bank deposit <amount>
/bank withdraw <amount>
```

## Arkovia blockchain wallet

A separate Arkovia account linked to the stable TShock user ID.

```text
/arkos balance
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
```

Never type a recovery phrase, private key or transaction PIN into normal Terraria chat.

For native ARKOS accounting:

```text
1 ARKOS = 100,000,000 atomic units
```

Internal Wallet/Bank accounting uses integer atomic units rather than floating-point balances.

---

# Gameplay economy

Arkovia Economy includes configurable:

- NPC rewards;
- treasury solvency enforcement;
- death Wallet deductions;
- protected Bank balances;
- PvP redistribution;
- floating positive/negative feedback;
- event pools;
- progression ranks;
- quests;
- jobs.

Frequent gameplay rewards remain off-chain. Killing an NPC does not create a blockchain transaction.

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

Supported provider integrations include Terraria-Servers.com and TServerWeb with configurable treasury-backed currency, item and temporary-group rewards.

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

The signing service is intentionally separated from TShock. Signing credentials and API keys must remain in protected server-side environment/configuration storage and must never be committed to GitHub.

See [`docs/BLOCKCHAIN_SETUP.md`](docs/BLOCKCHAIN_SETUP.md) and [`docs/SECURITY.md`](docs/SECURITY.md).

---

# Installation

## Arkovia Economy

Requirements:

- compatible Terraria/TShock server;
- .NET 9 runtime for the current TShock deployment;
- SQLite or another supported TShock database provider.

Install:

```text
1. Download/build ArkoviaEconomy.dll.
2. Place it in ServerPlugins/.
3. Restart TShock.
4. Review generated configuration.
5. Configure player/staff permissions.
```

## Arkovia Crossplay companion

Build:

```bash
dotnet build crossplay/ArkoviaCrossplay/ArkoviaCrossplay.csproj -c Release
```

Install:

```text
1. Build/download ArkoviaCrossplay.dll.
2. Place it in ServerPlugins/ beside ArkoviaEconomy.dll.
3. Restart TShock fully.
4. Run /arcrossplay info.
5. Use /arcrossplay verbose during mobile compatibility testing.
```

Do not treat arbitrary Terraria versions as compatible merely because they can be added to the allow-list.

## Marketplace service

Source:

```text
services/ArkoviaMarketplace/
```

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

The subject secret must be persistent. Changing it changes derived web subjects and can break existing links.

The TShock REST token should use a dedicated least-privilege TShock group.

---

# Important permissions

Core economy:

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

Marketplace REST:

```text
arkoviaeconomy.api.marketplace.read
arkoviaeconomy.api.marketplace.link
arkoviaeconomy.api.marketplace.write
```

Crossplay administration:

```text
arkovia.crossplay.admin
```

Economy administration:

```text
arkoviaeconomy.admin
arkoviaeconomy.admin.adjust
arkoviaeconomy.admin.treasury
arkoviaeconomy.admin.config
arkoviaeconomy.admin.audit
arkoviaeconomy.admin.vote
```

See [`docs/COMMANDS.md`](docs/COMMANDS.md).

---

# Security invariants

- No supported balance mutation without the ledger/settlement system.
- No externally requested mutation without authenticated identity and server-side authorization.
- No settlement based only on browser-supplied values.
- No ownership transfer before settlement completion.
- No plaintext private keys, recovery phrases or PINs in logs, URLs or normal configuration.
- Marketplace sessions use HttpOnly cookies, SameSite restrictions, CSRF protection, rate limiting and server-side trust decisions.
- TShock REST should remain firewalled/private rather than exposed directly to the public Internet.
- Crossplay is allow-list based and fails closed for unknown protocols.
- Command aliases should not shadow unrelated TShock commands by default.

Review [`SECURITY.md`](SECURITY.md) and [`docs/SECURITY.md`](docs/SECURITY.md) before production deployment.

---

# Testing

Typical validation:

```bash
dotnet build -c Release
dotnet run --project tests/ArkoviaEconomy.Tests.csproj -c Release
node tests/portal_ui_smoke.js
node tests/marketplace_web_smoke.js
dotnet build services/ArkoviaMarketplace/ArkoviaMarketplace.csproj -c Release
dotnet build crossplay/ArkoviaCrossplay/ArkoviaCrossplay.csproj -c Release
```

The crossplay companion was built on the production Arkovia host with **0 warnings and 0 errors** before deployment. It then successfully initialized alongside TShock 6.1 on Terraria 1.4.5.8.

Real-device mobile testing remains required before describing every 1.4.5.x mobile build as fully validated.

---

# Project direction

Planned platform phases include:

1. continued economy hardening;
2. towns/claims expansion;
3. businesses and property;
4. smart-region automation;
5. marketplace/escrow expansion;
6. rentals;
7. companies and shares;
8. internal secondary exchange/order book;
9. staff/character/shortcut tooling;
10. continued PC/mobile crossplay validation and future console integration when platform networking allows it.

See [`docs/PLATFORM_ROADMAP.md`](docs/PLATFORM_ROADMAP.md).

---

# License and contribution

Use the repository license for source distribution terms. Keep credentials, private keys, recovery phrases, marketplace secrets and server tokens out of commits, issues and screenshots.
