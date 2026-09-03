# Arkovia Economy — Configuration

This document explains the major configuration areas used by Arkovia Economy for TShock.

> Keep production credentials, recovery artifacts, private keys, secret phrases, runtime databases, and deployment-specific secrets out of source control.

---

## Currency Presentation

The native/default project currency is ARKOS.

```json
{
  "CurrencyName": "ARKOS",
  "CurrencySymbol": "ARKOS",
  "Decimals": 8
}
```

Balances use integer atomic units internally.

For eight decimal places:

```text
1 ARKOS = 100,000,000 atomic units
```

Operators may change the displayed currency name and symbol for their own Arkovia-based project.

Changing the display name or ticker does not automatically create a new blockchain currency, change the connected network, or alter blockchain address formatting.

---

## Gameplay Economy

Gameplay economy settings control rewards and losses that remain off-chain inside Terraria.

Current gameplay configuration includes:

```text
GameplayEconomy.Enabled
GameplayEconomy.DefaultBroadcastMode
GameplayEconomy.Rewards
GameplayEconomy.Death
GameplayEconomy.PvP
```

Supported default broadcast behavior includes player-facing economy messages while gameplay state remains in the internal ledger.

---

## Gameplay Reward Ranges

The reward configuration contains ranges for:

```text
CommonEnemy
StrongRareEnemy
EarlyBoss
MidBoss
EndGameBoss
Quest
```

Useful starting ranges for ARKOS testing are:

| Activity | Suggested range |
|---|---:|
| Common enemy | 0.0001 - 0.001 |
| Strong / rare enemy | 0.005 - 0.05 |
| Early boss | 0.10 - 0.25 |
| Mid-game boss | 0.25 - 0.50 |
| End-game boss | 0.50 - 1.00 |
| Quest | 0.05 - 0.25 |

These are balancing starting points, not fixed economic requirements.

Custom currencies should be scaled according to their own intended economy.

---

## Death Economy

The death configuration controls off-chain Wallet deductions caused by normal Terraria deaths.

Recommended starting behavior:

```text
Normal death penalty: 25% of Wallet (PenaltyPercent: 25)
Protected minimum:    0
Cooldown:             60 seconds
```

Important behavior:

- losses are taken from the gameplay Wallet only
- Bank funds remain protected
- balances never go below zero
- percentage is calculated from the full Wallet, rounded down to atomic units, and capped at Wallet minus MinimumProtectedBalance
- normal death loss returns to the internal treasury

---

## PvP Economy

PvP can use a separate configurable Wallet deduction.

A useful starting penalty is:

```text
0.01 ARKOS
```

The collected amount may be split between the winning player and the treasury according to configuration.

Only the actual amount available in the victims gameplay Wallet can be distributed.

Protected Bank funds and blockchain balances are not used for PvP penalties.

---

## Floating Currency Feedback

The current source can display signed native Terraria floating/combat text above the affected player.

Examples:

```text
+0.00100000
-0.00500000
```

Positive changes are displayed in green and negative changes in red.

The floating text shows the signed number only; the accompanying chat message can include the configured currency symbol and reason.

The current implementation does not yet expose a dedicated floating-text configuration section.

---

## Treasury Configuration

Arkovia Economy can enforce treasury-backed rewards.

When solvency enforcement is active, reward operations should fail instead of creating unsupported gameplay currency when the treasury cannot fund the transaction.

Server operators should size reward ranges relative to available treasury funding and expected player activity.

---

## Arkovia Blockchain Connection

A typical local Arkovia node endpoint is:

```json
"NodeUrl": "http://127.0.0.1:4876/nxt"
```

Localhost is preferred when the blockchain node and Terraria server run on the same trusted host.

The public example configuration should contain only public blockchain information and safe connection settings.

Do not place secret phrases, private keys, wallet passwords, signing credentials, or internal service API keys in the normal economy configuration.

---

## Community & Development Funding

The native Arkovia project uses this public Community & Development account:

```text
ARK-KVFL-C6EE-2UD2-CSJ8Q
```

The native Arkovia fee-distribution design allocates eligible block fees as:

```text
80% -> block forger
15% -> Network Treasury
 5% -> Community & Development
```

The plugin synchronizer watches the Community & Development account using account-ledger events.

The expected funding event type is:

```text
BLOCK_GENERATED
```

The synchronizer is read-only with respect to this funding account. It does not require the accounts secret phrase or private key.

---

## Wallet Creation and Recovery

Player blockchain wallet creation is separate from normal gameplay balances.

Public wallet identity can be stored with the TShock account.

Private recovery material is handled through the protected wallet-recovery workflow and must not be stored in the ordinary economy database.

The current recovery integration uses a separate local claim service in supported deployments.

Never publish its API credential or recovery files.

---

## Custom Arkovia-Based Projects

Operators adapting the plugin for their own Arkovia-compatible project should review:

1. `CurrencyName`
2. `CurrencySymbol`
3. decimal behavior
4. node endpoint
5. treasury/funding account
6. expected ledger event behavior
7. account/address format
8. wallet-generation compatibility
9. treasury economics
10. security isolation

Changing only the Terraria display ticker is not sufficient to redefine the underlying blockchain.

---

## Planned Configuration Areas

The following capabilities are planned or still evolving and should not be assumed to be available merely because they appear on the roadmap:

- blockchain deposits
- blockchain withdrawals
- outgoing transaction fee quoting
- signing bridge settings
- starter wallet grants
- ARKOS security PIN
- contribution-based boss rewards
- additional invasion/event reward pools
- expanded floating-text controls
- broader player balance privacy controls

---

## Safe Configuration Workflow

For production servers:

1. back up the economy database and configuration
2. edit configuration on a test server first
3. validate JSON before restart
4. restart or use `/eco reload` where supported
5. test `/balance`, `/treasury`, rewards, death, and PvP with small values
6. test blockchain commands only against the intended trusted node
7. review logs for errors without exposing secrets


## Currency selection and safe upgrades

```json
{
  "CurrencyId": "",
  "AcceptExistingBalancesForCurrencyChange": false,
  "Decimals": 8
}
```

- Blank `CurrencyId` selects native ARKOS. A positive numeric ID selects a Monetary System currency on the configured Arkovia node; symbols such as `VELR` are not IDs.
- A custom ID is validated with `getCurrency` at startup, including when automatic funding is disabled. Name, code, and blockchain decimals come from the node. Invalid metadata or an unreachable node stops initialization before gameplay/commands are registered; there is no silent native fallback.
- On-chain balances use `getAccountCurrencies` and confirmed `units` for custom currencies. Native balances use `getAccount` and native atomic units. A valid account with no selected currency returns zero.
- `Decimals` is the off-chain storage scale, normally **8**. It is independent of blockchain decimals. For example, 123 on-chain units of a two-decimal currency become 123,000,000 off-chain atomic units (1.23 currency). Tiny gameplay rewards can still use eight decimals. Conversion checks overflow and rounds down when reducing precision.
- Funding ledger queries filter `CURRENCY_BALANCE` plus the selected ID. The native default `BLOCK_GENERATED` event automatically becomes `CURRENCY_TRANSFER` for custom currencies. Other explicitly configured event types are retained. Configure the source account to one that actually holds the selected currency.
- Custom funding keys and balance baselines are isolated by currency and source. Existing native ledger keys and the legacy native high-water mark are preserved. The existing empty-ledger balance-growth fallback establishes a baseline without crediting the first observation; it still does not provide the ledger path's confirmation-depth guarantee.

### Existing installations

1. Stop the server and back up its database and configuration.
2. Replace the plugin DLL. Keep `Decimals` unchanged. Existing wallet, bank, treasury, history, and linked public wallet records are retained.
3. Replace the old ordinary-death `Penalty` setting with `PenaltyPercent` (0–100). If omitted, the new default is 25%; the old fixed `Penalty` no longer controls ordinary deaths. PvP retains its separate fixed `Penalty`, winner percentage, and treasury percentage.
4. For a currency change, set `CurrencyId` and explicitly set `AcceptExistingBalancesForCurrencyChange` to `true`. This retains numeric balances and relabels them in the selected currency; it does **not** perform an exchange or create on-chain backing. Without that opt-in the change is rejected.
5. Restart. After a successful change, set the acceptance flag back to `false`. The previous denomination is recorded in database state. Unmarked legacy databases are assumed to use native currency at eight decimals; installations with a different historical scale require a reviewed migration before upgrade.
6. Currency, off-chain decimals, node URL, and funding source changes require a restart. Invalid reloads retain the active configuration. Changing the stored off-chain scale is rejected even with the acceptance flag.

Ordinary death movements and admin adjustments commit their balance changes and audit entry in one database transaction. PvP continues to use its existing independent payout path.
