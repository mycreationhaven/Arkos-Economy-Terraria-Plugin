# Arkovia Economy — Commands & Permissions

This document describes the current player and administrator commands provided by Arkovia Economy for TShock.

> Players must be logged into a TShock account before using account-based economy or blockchain-wallet commands.

---

## Gameplay Balances

```text
/balance
/bal
/money
```

Shows the authenticated players off-chain Terraria economy balances:

- Wallet
- Bank
- Total

Permission:

```text
arkoviaeconomy.use
```

Authorized administrators with `arkoviaeconomy.admin.audit` may use the supported account argument to inspect another TShock accounts economy balance.

General player-to-player balance lookup is not currently exposed as a normal player permission.

---

## Player Payments

```text
/pay <account> <amount>
```

Transfers off-chain gameplay currency between authenticated TShock economy accounts.

Permission:

```text
arkoviaeconomy.pay
```

This is an internal Terraria economy transfer. It is not an Arkovia blockchain transaction.

---

## Bank

```text
/bank balance
/bank deposit <amount>
/bank withdraw <amount>
```

Permission:

```text
arkoviaeconomy.bank
```

The Bank is protected off-chain savings.

`deposit` moves funds from gameplay Wallet to Bank.

`withdraw` moves funds from Bank to gameplay Wallet.

Neither command deposits to or withdraws from the Arkovia blockchain.

---

## Economy History

```text
/econhistory [count]
```

Shows recent ledger activity for the authenticated economy account.

Permission:

```text
arkoviaeconomy.use
```

---

## Treasury

```text
/treasury
```

Shows treasury information available to the caller.

Permission:

```text
arkoviaeconomy.treasury.view
```

---

## Arkovia Blockchain Wallet

```text
/arkos balance
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
```

Permission:

```text
arkoviaeconomy.wallet
```

### `/arkos balance`

Queries the actual on-chain balance for the players linked public Arkovia account.

### `/arkos wallet create`

Creates a new Arkovia wallet and links its public wallet identity to the authenticated TShock User ID.

The private recovery material is handled through the protected recovery workflow rather than being stored in the ordinary economy database.

### `/arkos wallet address`

Shows public wallet information such as the Arkovia address and account ID.

### `/arkos wallet status`

Shows whether the player has a linked Arkovia wallet and its public status information.

### `/arkos wallet recovery`

Requests a secure recovery claim through the configured recovery infrastructure.

> Never type an Arkovia secret phrase or private key into Terraria chat or a normal TShock command.

---

## Administrator Commands

Current administrative operations include:

```text
/eco reload
/eco sync
/eco give <user> <amount> <reason>
/eco take <user> <amount> <reason>
/eco freeze <user>
/eco unfreeze <user>
/eco reward <user> <amount> <reason>
```

Administrative changes are designed to remain visible in the economy ledger.

### Permissions

```text
arkoviaeconomy.admin
arkoviaeconomy.admin.adjust
arkoviaeconomy.admin.treasury
arkoviaeconomy.admin.config
arkoviaeconomy.admin.audit
```

---

## Complete Permission Reference

```text
arkoviaeconomy.use
arkoviaeconomy.pay
arkoviaeconomy.bank
arkoviaeconomy.shop
arkoviaeconomy.market
arkoviaeconomy.jobs
arkoviaeconomy.treasury.view
arkoviaeconomy.wallet
arkoviaeconomy.admin
arkoviaeconomy.admin.adjust
arkoviaeconomy.admin.treasury
arkoviaeconomy.admin.config
arkoviaeconomy.admin.audit
```

The wildcard permission may be used for server-owner roles where appropriate:

```text
arkoviaeconomy.*
```

---

## Gameplay Economy Events

Players do not need to run commands to receive configured gameplay rewards.

Current gameplay integrations include eligible NPC rewards, normal death deductions, and PvP economic redistribution.

These operations use the off-chain gameplay ledger and do not create a blockchain transaction for every gameplay event.

Old Ones Army / DD2 contribution tracking is experimental. DD2 completion payout is currently disabled pending atomic multiplayer treasury payout support.

---

## Custom Currency Note

Operators can customize the displayed currency name and symbol.

The blockchain command namespace currently remains:

```text
/arkos
```

Changing `CurrencyName` or `CurrencySymbol` does not automatically rename that command namespace or alter the underlying blockchain protocol/address format.
