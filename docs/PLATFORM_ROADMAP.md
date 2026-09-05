# Arkovia Terraria Platform Roadmap

This roadmap expands Arkovia Economy from an economy plugin into a modular Terraria/TShock platform while keeping the financial ledger, permissions, and network boundaries secure.

## Guiding architecture

The TShock plugin remains authoritative for in-game identity, inventory, region membership, and world interactions. Arkovia backend services remain authoritative for online marketplace workflows, escrow, settlement state, and web-facing APIs. Browser input is never trusted for ownership, balances, prices, permissions, or settlement completion.

## Phase 0 — hardening and release foundation

- Finish the vote-reward command-collision fix and diagnostics.
- Make every balance-changing operation atomic with its ledger records.
- Add idempotency/replay protection to external and marketplace-triggered operations.
- Audit permissions and command aliases for collisions and privilege escalation.
- Audit network calls, secrets, timeout handling, and server-authoritative validation.
- Add regression tests for transfers, fees, bank operations, rewards, PvP, vote claims, and failure rollback.

## Phase 1 — towns, claims, property, and businesses

Inspired by Towny concepts but implemented natively for Terraria/TShock:

- Towns, residents, invitations, mayor/assistant roles, town treasury, taxes, and configurable limits.
- Region-backed land claims with build/use/access policies and overlap safety.
- Property assets representing land, houses, shops, and other transferable regions.
- Businesses with owners, staff roles, treasury accounts, storefront/property links, and status.
- Every transferable world object receives a stable asset ID so it can later be listed without redesigning ownership.

## Phase 2 — smart regions and command automation

Inspired by SmartRegions and ShortCommands:

- Safe region actions: message, heal, buff, give item, teleport, economy reward/charge, town enter/exit, team changes.
- Optional administrator-enabled raw command actions with strict permissions and audit logs.
- Per-player/per-region cooldowns and overlap priority.
- Configurable short commands/macros with positional arguments and remaining-argument expansion.
- Refuse command collisions by default; explicit administrator override required.

## Phase 3 — online marketplace

Target web route: `/marketplace` on the Arkovia service.

Marketplace asset categories:

- Terraria items and collectibles.
- Land and houses for sale.
- Land and houses for rent or lease-to-own.
- Businesses for sale or rent.
- Services and other approved asset types.

Core state machine:

`Draft -> Active -> Reserved -> PendingPayment -> Escrowed -> Settling -> Completed`

Terminal/exception states:

`Cancelled`, `Expired`, `Disputed`, `Failed`.

Requirements:

- Server-authoritative ownership and price validation immediately before settlement.
- Escrow/reservation so the same item/property cannot be sold twice.
- Atomic ARKOS settlement and ownership transfer.
- Idempotency keys for every externally initiated mutation.
- Full audit history with immutable transaction references.
- No direct public access to TShock or its database.

Recommended boundary:

`Marketplace Website -> Arkovia Marketplace API -> authoritative database/settlement service -> authenticated Terraria connector -> TShock world`

## Phase 4 — companies and internal share exchange

The exchange is an internal Arkovia Network economy feature, not an external securities exchange.

- Companies with treasury accounts, officers, shares outstanding, and shareholder ledgers.
- Buy and sell limit orders, partial fills, cancellation, and trade history.
- Price history derived from executed trades rather than arbitrary price changes.
- Optional dividends paid from company treasury through the same atomic ledger engine.
- Portfolio views in-game and on the website.
- Strong anti-double-spend, order-reservation, and replay protection.

## Phase 5 — character/staff tooling

Inspired by CharacterReset and Ghost:

- Character reset by category (stats, inventory, quests, banks) with permissions and destructive-action confirmation.
- Character resets must never erase Arkovia wallet, blockchain identity, transaction history, marketplace ownership, town membership, or company holdings unless a separate explicit feature is designed for that purpose.
- Audited staff ghost mode with configurable player-list, join/leave, chat, PvP, NPC, and teleport visibility behavior.

## Phase 6 — crossplay integration

Crossplay packet translation should remain a separate compatibility module or integration layer because Terraria protocol changes can break packet translation independently of the economy. The core economy/towns/marketplace plugin should detect and interoperate with a compatible crossplay plugin instead of tightly coupling protocol translation to financial code.

## Security invariants

1. No balance mutation without a matching atomic ledger transaction.
2. No externally initiated mutation without authentication, authorization, idempotency, and audit metadata.
3. No marketplace settlement based solely on browser-submitted balances, prices, ownership, or permissions.
4. No transfer of land/property/business ownership until settlement succeeds.
5. No command alias may shadow an existing TShock/Arkovia command by default.
6. No destructive character action may touch Arkovia economic identity by accident.
7. No web service receives unrestricted direct SQL/TShock access.
