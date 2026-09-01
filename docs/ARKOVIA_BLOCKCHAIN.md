# Arkovia blockchain integration

## Source-verified fee distribution

The supplied Arkovia source implements fee distribution in `src/java/nxt/BlockImpl.java`. Once block height is at least 1500 and distributable fees are positive, the code computes:

```text
forgerFee = distributableFee * 80 / 100
networkTreasuryFee = distributableFee * 15 / 100
communityDevelopmentFee = remainder (5%)
```

The destinations in that source are:

- 15% Network Treasury: `ARK-73PZ-GB9A-5BP7-22UZU`
- 5% Community & Development: `ARK-KVFL-C6EE-2UD2-CSJ8Q`

The 5% credit is applied with the ledger event `BLOCK_GENERATED`.

## Why the plugin uses getAccountLedger

The 5% distribution is not a normal transaction sent to the treasury account. It is a consensus-level balance change generated while a block is applied. Therefore scanning `getBlockchainTransactions` would miss those credits.

The plugin calls:

```text
requestType=getAccountLedger
account=ARK-KVFL-C6EE-2UD2-CSJ8Q
eventType=BLOCK_GENERATED
holdingType=NXT_BALANCE
```

It also calls `getBlockchainStatus` to determine current height and calculate confirmations.

## Duplicate protection

Each confirmed funding credit is assigned a deterministic key using treasury account, block ID, event ID and change amount. Before crediting the Terraria Treasury, the plugin checks `ArkoviaEconomyFunding`. Once inserted, the same confirmed event is not credited again.

## Reorganizations

The confirmation threshold is the primary reorganization defense. Because Arkovia account ledger IDs are peer-local and may change after rollback, the plugin does not use `ledgerId` as its economic identity. Only sufficiently confirmed positive `BLOCK_GENERATED` credits are accepted.

## Ledger retention

Your supplied `nxt-default.properties` currently includes:

```text
nxt.ledgerAccounts=*
nxt.ledgerLogUnconfirmed=2
nxt.ledgerTrimKeep=30000
```

This is adequate for a frequently polling plugin. If a production node changes ledger tracking or retention, keep the treasury account tracked and ensure polling occurs frequently enough that credits are observed before entries are trimmed.

## No signing

This plugin performs read-only blockchain calls. It does not submit transactions, forge, store keys, unlock accounts, or sign withdrawals. A future withdrawal bridge should be a separate hardened service with explicit limits and signing isolation.
