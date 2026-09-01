# Database and ledger

Arkovia Economy uses the same `TShock.DB` connection that TShock exposes, allowing deployment with TShock's supported database backend.

## Tables

### ArkoviaEconomyAccounts
Stores player and system accounts. Player accounts reference the stable TShock User ID, not the Terraria character name.

Important fields: `TShockUserId`, `AccountType`, `Name`, `WalletAtomic`, `BankAtomic`, `Frozen`.

### ArkoviaEconomyTransactions
Append-only economic audit ledger. Every movement or administrative adjustment receives a unique `ExternalId`, references source/destination accounts, amount, transaction type, external reference, description, actor, and timestamp.

### ArkoviaEconomyFunding
Records Arkovia blockchain ledger entries already recognized as game funding. `ExternalKey` is the idempotency key that prevents duplicate funding.

### ArkoviaEconomyState
Stores synchronization metadata such as last observed Arkovia height.

### ArkoviaEconomyShops
Schema reserved for first-party server-shop definitions: shop key, Terraria item ID, prefix, buy/sell price and stock.

### ArkoviaEconomyMarket
Schema reserved for player-market listings and escrow workflows.

## Atomic values

Money is stored as signed 64-bit integer atomic values rather than floating point values. At 8 decimals:

```text
1 ARK = 100,000,000 atomic
0.01 ARK = 1,000,000 atomic
```

This avoids floating-point rounding errors in financial operations.
