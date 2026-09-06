# 🌎 Arkovia Economy for Terraria / TShock

> **README REFRESH — September 6, 2026:** Arkovia Crossplay is now built, deployed, and included in the v1.5 release workflow alongside the main economy plugin.

Arkovia Economy is a server-authoritative Terraria/TShock economy and community platform combining Wallet/Bank accounting, towns and property, a live web marketplace, item escrow, stock holdings, voting rewards, progression, optional Arkovia blockchain settlement, and a separate crossplay compatibility companion.

**Release:** `v1.5.0-rc.1`  
**Economy plugin:** `ArkoviaEconomy.dll`  
**Crossplay companion:** `ArkoviaCrossplay.dll`  
**Live server:** Terraria `1.4.5.8` / protocol `Terraria319`  
**Stack:** TShock 6.1 / .NET 9  
**Marketplace:** https://arkovia-node1.mywire.org/marketplace  
**Short URL:** https://arkovia-node1.mywire.org/market

## Platform status

| Platform | Status |
|---|---|
| PC | ✅ Supported |
| Mobile 1.4.5.x | ✅ Crossplay bridge deployed; real-device gameplay validation ongoing |
| Xbox | ⏳ Not enabled by the handshake bridge alone |
| PlayStation | ⏳ Not enabled by the handshake bridge alone |
| Nintendo Switch | ⏳ Not enabled by the handshake bridge alone |

The console distinction matters: Xbox, PlayStation and Switch have platform networking/discovery restrictions beyond Terraria's version handshake. Arkovia Crossplay does **not** bypass platform networking or create console custom-IP joining.

---

## Major v1.5 features

- Atomic integer Wallet/Bank accounting and immutable ledger.
- Treasury-backed gameplay rewards, death penalties and PvP economy.
- Paid ranks, quests and jobs.
- Vote rewards using `/arkvote` / `/voterewards`.
- Town creation, membership, governance and treasury.
- TShock region/property claims and property marketplace settlement.
- Stable transferable assets using `ARK-ASSET-*` IDs.
- Secure marketplace reservations, escrow and settlement.
- Six-digit, five-minute marketplace account-link/re-authentication codes.
- Live online Terraria inventory on the website.
- Website inventory listing by slot, quantity and total ARKOS price.
- Item escrow and claim-to-Terraria delivery.
- Player marketplace profiles and purchase/listing history.
- Stock quotes, holdings and primary share purchases.
- Clickable scrolling ARKOVIA EXCHANGE stock marquee.
- Same-origin cached Terraria item artwork proxy.
- Companion Arkovia Crossplay Bridge for approved Terraria 1.4.5.x PC/mobile protocols.

The stock system is currently a **primary-offering foundation**. Player-to-player bid/ask matching, locked shares/funds and price-time-priority secondary trading remain planned.

---

# Crossplay

Source project:

```text
crossplay/ArkoviaCrossplay/
```

Build output:

```text
ArkoviaCrossplay.dll
```

The bridge runs alongside `ArkoviaEconomy.dll`. It observes the initial Terraria `ConnectRequest` and rewrites only explicitly approved 1.4.5.x client protocol handshakes to the server's current protocol. It is deliberately fail-closed: unknown protocols are left to normal TShock/Terraria validation.

### Current approved protocols

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

The live Arkovia server currently reports:

```text
Terraria v1.4.5.8
Terraria319
```

### Commands

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

Alias: `/acp`

Use `/arcrossplay verbose` during mobile testing to record the exact client protocol presented during connection.

### Important crossplay limitation

This is a **handshake compatibility bridge**, not a universal packet translator. A client passing version validation does not prove every gameplay packet is compatible. Mobile testing should cover login/SSC, movement, inventory, chests, item pickup/drop, NPCs, combat/projectiles, world sections, marketplace identity and reconnect behavior before broadening the allow-list.

---

# Marketplace

Production marketplace:

```text
https://arkovia-node1.mywire.org/marketplace
```

`/market` redirects to `/marketplace`.

Link/sign in from Terraria with:

```text
/market link
```

The code is cryptographically generated, six digits, single-use, expires after five minutes and has bounded failed attempts. Already-linked accounts can request a fresh code without replacing their durable marketplace identity.

While the player is online, the marketplace can show the authoritative live Terraria inventory. A listing request identifies a slot and quantity, then TShock re-reads the slot, validates the item/stack/favorite state, removes the listed quantity and creates marketplace item escrow. Browser-provided item information is never treated as proof of ownership.

Purchased or returned items can be claimed through the website or:

```text
/claimitems
```

Marketplace browser sessions use server-side linked identity, HttpOnly/SameSite cookies, CSRF protection, rate limiting and idempotency. The browser never receives the TShock REST token.

---

# Stocks / Arkovia Exchange

Player commands:

```text
/stocks
/stocks market
/stocks mine
/stocks portfolio
/stocks buy <ticker> <shares>
```

Alias: `/stock`

Administrator setup:

```text
/stockadmin create <ticker> <name> <price> <shares>
/stockadmin price <ticker> <price>
```

Primary purchases verify the current quote, shares and Wallet balance, then atomically debit the buyer, credit the issuer, update available shares/holdings and write the economy ledger.

---

# Towns and property

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

Property marketplace:

```text
/market listings
/market info <listingId>
/market sellproperty <region> <price>
/market buy <listingId>
/market cancel <listingId>
```

Property settlement guards escrow, seller town treasury, configured tax, asset/property ownership, transfer audit, marketplace sale and TShock region ownership/ACL state.

---

# Economy and blockchain

Gameplay Wallet is spendable internal currency. Gameplay Bank is protected internal savings. Native ARKOS accounting uses integer atomic units:

```text
1 ARKOS = 100,000,000 atomic units
```

Bank commands:

```text
/bank balance
/bank deposit <amount>
/bank withdraw <amount>
```

Arkovia blockchain identity is separate from the gameplay Wallet/Bank:

```text
/arkos balance
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
/arkos deposit
/arkos withdraw
/arkos transfers
```

Never place recovery phrases, private keys, signing credentials or transaction PINs in normal Terraria chat, source control or public configuration.

---

# Voting

Arkovia avoids TShock's built-in `/vote` poll command collision:

```text
/arkvote links
/arkvote claim [provider]
/arkvote status
/arkvote debug
/arkvote tserverweb [captcha-answer]
```

Alias: `/voterewards`

See `docs/VOTING.md`.

---

# Installation

### Economy plugin

```text
1. Download ArkoviaEconomy.dll from the GitHub release.
2. Place it in ServerPlugins/.
3. Restart TShock fully.
4. Review generated configuration and permissions.
```

### Crossplay companion

```text
1. Download ArkoviaCrossplay.dll from the GitHub release.
2. Place it beside ArkoviaEconomy.dll in ServerPlugins/.
3. Restart TShock fully.
4. Run /arcrossplay info.
5. Enable /arcrossplay verbose while validating mobile clients.
```

Build from source:

```bash
dotnet build -c Release
dotnet build crossplay/ArkoviaCrossplay/ArkoviaCrossplay.csproj -c Release
dotnet build services/ArkoviaMarketplace/ArkoviaMarketplace.csproj -c Release
```

---

# Security invariants

1. No supported balance mutation without ledger/settlement accounting.
2. No external mutation without authenticated identity, authorization, idempotency and auditability.
3. Never settle from browser-supplied authoritative values.
4. Never transfer ownership before settlement succeeds.
5. TShock REST and signing credentials stay server-side.
6. Crossplay accepts only explicit protocol allow-list entries and does not claim console networking support it does not provide.
7. Destructive character tooling must never silently destroy economic identity.

See `SECURITY.md`, `docs/SECURITY.md`, `docs/COMMANDS.md`, `docs/PLATFORM_ROADMAP.md`, and `crossplay/ArkoviaCrossplay/README.md`.

---

# Roadmap

Planned work includes mobile compatibility validation, stronger item delivery state handling, rentals, companies/businesses, smart-region automation, a secondary stock order book, staff/character/shortcut tooling, and future console integration when Terraria/platform networking exposes a supported connection path.

## License

See `LICENSE`.
