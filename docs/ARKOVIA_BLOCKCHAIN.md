# Arkovia Economy — Blockchain Integration

This document explains how Arkovia Economy interacts with the Arkovia blockchain and clearly separates currently implemented blockchain behavior from planned features.

---

## Overview

Arkovia Economy combines two related economic layers:

1. an off-chain Terraria economy used for frequent gameplay transactions
2. Arkovia blockchain accounts used for real on-chain ownership and balance lookup

Gameplay rewards, death penalties, PvP transfers, banking, and most everyday server transactions remain off-chain.

Blockchain interaction is reserved for operations that actually require the Arkovia network.

---

## Native Currency

The native/default currency is:

```text
ARKOS
```

The plugin uses eight-decimal atomic accounting internally.

```text
1 ARKOS = 100,000,000 atomic units
```

Custom projects may change the displayed currency name and symbol, but display customization does not itself create a new blockchain asset or change the Arkovia network.

---

## Node Connection

A typical local node endpoint is:

```text
http://127.0.0.1:4876/nxt
```

When the Terraria server and Arkovia node run on the same trusted machine, localhost is preferred.

The configured node should not expose sensitive administrative functionality to untrusted networks.

---

## Community & Development Funding Account

The native Arkovia project Community & Development account is:

```text
ARK-KVFL-C6EE-2UD2-CSJ8Q
```

The Arkovia fee-distribution design allocates eligible block fees as:

```text
80% -> block forger
15% -> Network Treasury
 5% -> Community & Development
```

The plugin can synchronize the Community & Development funding stream into the Terraria economy treasury.

---

## Treasury Funding Synchronizer

The treasury synchronizer is a read-only blockchain integration.

It watches the configured Community & Development account using account-ledger data and expects the relevant funding event type:

```text
BLOCK_GENERATED
```

The synchronizer does not require or use the Community & Development accounts secret phrase, private key, or signing credentials.

It reads public blockchain ledger information and records eligible funding into the internal Terraria treasury.

Configured confirmation requirements should be satisfied before eligible ledger entries are credited.

---

## Player Blockchain Wallets

Authenticated TShock players can create a personal Arkovia blockchain wallet from inside the game.

Current command namespace:

```text
/arkos
```

Current wallet-related commands include:

```text
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
/arkos balance
```

The public wallet record can be linked to the players stable TShock account ID.

Public information may include:

- Arkovia account ID
- Arkovia account address
- public key
- creation timestamp

Private recovery material must remain separate from the normal gameplay economy database.

---

## Wallet Generation Security

Wallet generation is not the same as the read-only treasury synchronizer.

During wallet creation, the plugin generates a cryptographically secure secret phrase and temporarily uses it with the configured local Arkovia node to derive the blockchain account.

The generated recovery secret is then handled by the protected wallet-recovery workflow.

The plugin must not store that secret phrase in:

- the ordinary TShock economy database
- normal configuration files
- gameplay transaction records
- console logs
- ordinary Terraria chat history

Public account information may be persisted after wallet creation.

The recovery secret exists only where required by the protected recovery workflow.

---

## Recovery Workflow

Supported deployments can use a separate local wallet-claim service for securely presenting recovery material.

The Terraria plugin and recovery service should remain separated from normal gameplay data.

Recovery artifacts and service credentials must never be committed to GitHub or included in public releases.

Players should never paste a real recovery secret, secret phrase, or private key into Terraria chat or an ordinary TShock command.

---

## On-Chain Balance Lookup

The plugin can query a linked players real Arkovia blockchain balance.

Example:

```text
/arkos balance
```

Balance lookup uses public account information only.

It does not require the players recovery secret or private key.

The blockchain node remains authoritative for the on-chain balance.

---

## Off-Chain Wallet and Bank

The gameplay Wallet and Bank are not blockchain accounts.

They are internal Terraria economy balances stored in atomic units.

```text
Gameplay Wallet = spendable off-chain balance
Gameplay Bank   = protected off-chain savings
Blockchain     = real Arkovia on-chain balance
```

Commands such as:

```text
/bank deposit
/bank withdraw
```

move value only between the internal gameplay Wallet and Bank.

They do not currently perform blockchain deposits or withdrawals.

---

## Current On-Chain Capabilities

Currently implemented blockchain-related capabilities include:

- Community & Development ledger synchronization
- confirmation-aware treasury funding
- player blockchain wallet creation
- public wallet identity persistence
- protected wallet recovery workflow
- secure recovery claim integration for supported deployments
- public on-chain balance lookup

---

## Not Yet Implemented

The following features are planned and must not be treated as active today:

- player blockchain deposits into the gameplay economy
- player blockchain withdrawals
- automatic outgoing ARKOS transaction signing
- withdrawal fee quoting and confirmation
- starter-wallet ARKOS grants
- general hot-wallet payout processing
- secure player PIN authorization

---

## Planned Deposit Architecture

A future deposit flow should work approximately as follows:

1. player sends ARKOS to the designated game reserve address
2. server observes the blockchain transaction
3. configured confirmations are reached
4. transaction is checked for replay or duplicate crediting
5. the players off-chain gameplay balance is credited atomically

The blockchain transaction must remain the authoritative source of truth.

---

## Planned Withdrawal Architecture

A future withdrawal flow should avoid storing signing credentials in the TShock plugin.

Recommended design:

1. verify player identity and security authorization
2. reserve the requested off-chain amount
3. obtain the actual blockchain transaction fee
4. show or validate amount, fee, and total
5. send a narrowly scoped request to a localhost signing service
6. signing service submits the blockchain transaction
7. record transaction ID and state
8. confirm the transaction
9. finalize the off-chain deduction

If submission fails before a transaction is accepted, the reserved off-chain amount should be safely released.

---

## Reserve Solvency

Any future withdrawable off-chain ARKOS should be backed by real on-chain reserves.

A future reserve report should be able to show values such as:

```text
on-chain reserve
off-chain liabilities
pending withdrawals
available reserve
coverage ratio
```

This helps prevent creation of unsupported withdrawable balances.

---

## Transaction Fees

The 80 / 15 / 5 distribution describes how eligible Arkovia fees are distributed.

It does not define the exact fee amount for every outgoing transaction.

Before future withdrawals or payouts are enabled, the plugin or signing service should obtain and validate the real fee required by the Arkovia network.

---

## Custom Arkovia-Based Currencies

Projects can customize Terraria-facing currency presentation through configuration.

However, changing:

```text
CurrencyName
CurrencySymbol
```

does not automatically change:

- the Arkovia blockchain protocol
- account/address format
- account derivation
- node API semantics
- blockchain asset ownership
- transaction signing rules

Projects using their own Arkovia-based currency should separately verify their network, funding account, node behavior, wallet-generation compatibility, fee policy, and reserve model.

The current player blockchain command namespace remains `/arkos` unless the plugin source is customized.

---

## Security Rules

1. never store blockchain secret phrases in the normal TShock economy database
2. never log recovery secrets or private keys
3. never place real secret phrases in ordinary Terraria commands or chat
4. keep recovery artifacts outside the public repository
5. keep wallet-claim service credentials outside source control
6. prefer localhost for trusted node and signing-service communication
7. separate public account data from private recovery material
8. treat the blockchain node as authoritative for on-chain state
9. use confirmation and idempotency protections before crediting deposits
10. keep any future signing service narrowly scoped and separately secured

---

## Integration Summary

```text
Terraria gameplay
      |
      v
Off-chain atomic ledger
      |
      +--> Wallet / Bank / rewards / PvP / treasury
      |
      +--> public wallet linkage
                 |
                 v
          Arkovia local node
                 |
                 +--> public balance lookup
                 +--> wallet account derivation
                 +--> Community & Development ledger sync
```

The design keeps frequent gameplay transactions fast and off-chain while allowing real Arkovia blockchain ownership where it is useful.
