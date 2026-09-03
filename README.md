# 🌎 Arkovia Economy for Terraria / TShock

> A treasury-backed Terraria economy with gameplay rewards, banking, PvP economics, player blockchain wallets, and optional Arkovia blockchain integration.

**Arkovia Economy** is an open-source economy plugin for Terraria servers running TShock.

It combines a fast **off-chain gameplay economy** with optional **real Arkovia blockchain accounts**.

Players can earn currency while playing Terraria, maintain Wallet and Bank balances, transfer funds, view transaction history, create an Arkovia blockchain wallet, securely recover it, and check its real on-chain balance.

Server operators can use the native **ARKOS** currency or customize the economy presentation for their own Arkovia-based project.

---

## ✨ Feature Status

| Feature | Status |
|---|---|
| TShock account-based economy | ✅ Available |
| 8-decimal atomic accounting | ✅ Available |
| Player Wallet balance | ✅ Available |
| Protected Bank balance | ✅ Available |
| Player-to-player payments | ✅ Available |
| Transaction history | ✅ Available |
| Treasury-backed rewards | ✅ Available |
| NPC kill rewards | ✅ Available |
| Initial boss reward classification | ✅ Available |
| Death penalties | ✅ Available |
| PvP economic rewards/losses | ✅ Available |
| Floating green/red currency text | ✅ Available |
| Arkovia treasury synchronization | ✅ Available |
| Player-created Arkovia wallets | ✅ Available |
| Secure wallet recovery workflow | ✅ Available |
| On-chain wallet balance lookup | ✅ Available |
| DD2 / Old Ones Army tracking | ✅ Implemented — staging validation |
| DD2 multiplayer reward payout | ✅ Implemented — staging validation |
| Blockchain deposits | ✅ Implemented — configuration required |
| Blockchain withdrawals | ✅ Implemented — configuration required |
| Automatic starter blockchain grant | ✅ Implemented — configuration required |
| ARKOS transaction PIN | ✅ Implemented — configuration required |
| Additional Terraria event rewards | ✅ Implemented — staging validation |

---

## 🎮 Two Economies Working Together

Arkovia Economy deliberately separates rapid Terraria gameplay transactions from blockchain transactions.

```text
Terraria gameplay
       │
       ▼
Off-chain economy ledger
       │
       ├── Player Wallet
       └── Player Bank

Arkovia blockchain
       │
       ▼
Player Arkovia Wallet
       │
       └── Real on-chain balance
```

Killing an enemy does **not** create a blockchain transaction.

Frequent gameplay activity remains in the fast internal economy ledger. Blockchain operations are reserved for actions that actually require the Arkovia network.

---

## 💰 Wallet vs Bank vs Blockchain Wallet

Arkovia Economy uses three distinct financial concepts.

### Gameplay Wallet

The gameplay Wallet contains spendable off-chain currency inside Terraria.

It can be used for player payments, gameplay systems, fees, PvP, shops, services, and other in-game activity.

Normal gameplay losses are taken from this balance.

### Gameplay Bank

The Bank is protected off-chain savings inside Terraria.

Players can move funds between Wallet and Bank with:

```text
/bank deposit <amount>
/bank withdraw <amount>
```

These commands do **not** move blockchain funds. They only move internal Terraria economy balances.

Normal death and PvP penalties do not drain the protected Bank balance.

### Arkovia Blockchain Wallet

The blockchain wallet is a real Arkovia account linked to the authenticated TShock user.

It has a public Arkovia address, account ID, public key, and private recovery secret.

The blockchain wallet is separate from the gameplay Wallet and Bank.

---

## ⚛️ Atomic Currency Accounting

Arkovia Economy stores balances using integer atomic units.

For native ARKOS:

```text
1 ARKOS = 100,000,000 atomic units
```

That provides eight decimal places of precision:

```text
0.00000001 ARKOS
```

Example:

```text
0.00044127 ARKOS = 44,127 atomic units
```

Using integer atomic units avoids storing economy balances as floating-point values.

---

## 🐲 Gameplay Rewards

Arkovia Economy can reward authenticated players for defeating eligible hostile Terraria NPCs.

Current reward categories include:

- Common Enemy
- Strong / Rare Enemy
- Early Boss
- Mid-Game Boss
- End-Game Boss
- Quest reward range for future integrations

Friendly NPCs and town NPCs are excluded.

Obvious statue-spawn farming is also rejected.

Boss classification currently uses an initial max-health based system. Explicit boss progression tiers and contribution-based multiplayer boss payouts are planned improvements.

Gameplay rewards are treasury-backed when treasury solvency enforcement is enabled.

If the treasury cannot fund a reward, the plugin fails closed instead of silently minting unsupported gameplay currency.

---

## 💚 Native Floating Currency Feedback

Currency changes can also appear above the player using Terrarias native combat-text networking.

Example gain:

```text
+0.00044127
```

Example loss:

```text
-0.00500000
```

Positive changes are shown in green and negative changes in red.

The floating display intentionally shows the signed number only. The normal chat message explains the reason and includes the configured currency symbol.

Example:

```text
You earned 0.00044127 ARKOS for killing Maggot Zombie.
```

No client-side Terraria mod is required for this native floating-text feedback.

---

## ☠️ Death Economy

Normal Terraria deaths deduct a configurable percentage of the gameplay Wallet (`GameplayEconomy.Death.PenaltyPercent`, default **25%**). Losses round down to whole atomic units and respect the protected minimum.

Important rules:

- Bank funds remain protected.
- Players cannot go below zero.
- The actual loss is clamped to the available Wallet balance.
- A configurable cooldown prevents repeated rapid penalties.
- Normal death deductions return funds to the internal treasury.

Example:

```text
Wallet before death: 100 ARKOS
Bank:                 50 ARKOS
Configured penalty:   25%

Actual loss:          25 ARKOS (credited to Terraria Treasury)
Wallet after death:   75 ARKOS
Bank after death:     50 ARKOS
```

---

## ⚔️ PvP Economy

PvP can use a separate configurable economic penalty.

The victim can lose only what is available in the gameplay Wallet.

The actual amount collected can be split between the winning player and the treasury.

This creates meaningful competitive rewards without touching protected Bank funds or the players blockchain wallet.

When PvP attribution is unavailable or invalid, the economy fails closed and no currency is moved.

---

## 🏦 Treasury-Backed Economy

One of the core design goals of Arkovia Economy is to avoid uncontrolled currency creation.

Gameplay rewards can be paid from the internal Terraria Treasury.

When treasury solvency enforcement is enabled, a reward is issued only when the treasury can fund it.

```text
Terraria Treasury
       │
       ▼
Gameplay rewards
       │
       ▼
Players
       │
       ├── server fees
       ├── services
       ├── death penalties
       └── other economy sinks
               │
               ▼
            Treasury
```

This allows server operators to build an economy around identifiable sources and sinks instead of treating every reward as unlimited money creation.

---

## ⛓️ Arkovia Blockchain Integration

Arkovia Economy can connect to an Arkovia blockchain node through the Arkovia HTTP API.

Example local node endpoint:

```text
http://127.0.0.1:4876/nxt
```

Using a private or localhost node endpoint is strongly recommended whenever the Terraria server and blockchain node are on the same trusted machine.

The plugin can monitor the configured Community & Development account for confirmed blockchain funding events and allocate an operator-configured percentage to the Terraria Treasury.

---

## 🧱 Arkovia Fee Distribution

The Arkovia blockchain implementation used by this project distributes eligible block fees as:

```text
80% -> Block Forger
15% -> Network Treasury
 5% -> Community & Development
```

The projects default Community & Development account is:

```text
ARK-KVFL-C6EE-2UD2-CSJ8Q
```

The Community & Development credit is an internal blockchain ledger event rather than an ordinary payment transaction.

For this reason, the treasury synchronizer monitors:

```text
requestType=getAccountLedger
eventType=BLOCK_GENERATED
```

rather than relying only on ordinary blockchain payment transactions.

The synchronizer also checks blockchain height so the configured confirmation requirement can be enforced.

Confirmed funding events receive duplicate protection before being credited to the Terraria Treasury.

---

## 👛 Player Arkovia Blockchain Wallets

Authenticated Terraria players can create their own Arkovia blockchain wallet from inside the game.

```text
/arkos wallet create
```

The plugin generates an Arkovia account and associates its public identity with the players stable TShock account ID.

The public wallet record can contain:

- TShock User ID
- Arkovia account ID
- Arkovia address
- public key
- creation timestamp

The private recovery secret is deliberately treated differently from ordinary gameplay data.

---

## 🔐 Wallet Recovery Security

An Arkovia recovery secret can control the associated blockchain wallet.

Anyone who obtains that secret may be able to control the account.

> **Never paste an Arkovia recovery secret or private key into Terraria chat or an ordinary TShock command.**

Terraria commands and server activity may be logged, so normal chat is not an appropriate secret-entry channel.

During wallet creation, recovery material is written through the protected recovery workflow rather than being stored as a normal economy database field.

The recovery material must be protected by appropriate filesystem permissions and deployment security.

The plugin must never log the recovery secret or wallet-claim API credential.

If secure claim creation fails after the blockchain wallet has already been created, the recovery workflow is designed to retain the protected recovery artifact rather than silently destroy the players ability to recover the wallet.

Server operators should review `SECURITY.md` and `docs/SECURITY.md` before enabling wallet creation on a public server.

---

## 🌐 Optional Secure Recovery Claim Service

The current wallet workflow supports integration with a separate local recovery-claim service.

The plugin-side integration communicates with the service through localhost:

```text
127.0.0.1:4890
```

This service is deployment infrastructure and is separate from the normal Terraria economy database.

Its internal API credential must remain private and must never be committed to this repository.

Production deployments should additionally protect recovery endpoints with appropriate transport security, filesystem permissions, expiration, rate limiting, and service isolation.

---

## 📊 Real On-Chain Balance Lookup

Players who have created an Arkovia wallet can query its actual blockchain balance:

```text
/arkos balance
```

The plugin queries the configured Arkovia node using the players linked public account.

Example response:

```text
On-chain balance: 143.00000000 ARKOS
Wallet: ARK-....
```

The on-chain balance is separate from the Terraria gameplay Wallet and Bank.

Checking a blockchain balance requires only the public account. It does not require the players secret phrase or private key.

---

## 🔄 What Is On-Chain Today?

The current plugin should not be interpreted as moving every gameplay transaction onto the blockchain.

### Available now

- Arkovia node connectivity
- confirmed treasury funding synchronization
- player blockchain wallet creation
- public wallet association with TShock accounts
- secure recovery workflow integration
- on-chain balance lookup

### New in 1.2.0 release candidate

Confirmed blockchain deposits, PIN-authorized withdrawals, fee review, a separate local signing service, and optional one-time starter grants are implemented. These features are disabled until configured; installing the DLL alone does not enable reserve spending.

Start with [blockchain and PIN setup](docs/BLOCKCHAIN_SETUP.md). The release includes `release/ArkoviaSigner.zip` alongside the updated plugin DLL. Network fees are paid by the operator's reserve. Existing Wallet and Bank amounts are preserved.

---

## 🎮 Player Commands

Players must be authenticated to a TShock account before using financial commands.

The economy identity is the stable **TShock User ID**, not merely the Terraria character name.

### Gameplay Economy Commands

| Command | Permission | Description |
|---|---|---|
| `/balance` | `arkoviaeconomy.use` | Show Wallet, Bank, and combined gameplay balance |
| `/bal` | `arkoviaeconomy.use` | Alias for `/balance` |
| `/money` | `arkoviaeconomy.use` | Alias for `/balance` |
| `/pay <account> <amount>` | `arkoviaeconomy.pay` | Transfer gameplay currency to another authenticated TShock account |
| `/bank balance` | `arkoviaeconomy.bank` | Show Wallet and Bank balances |
| `/bank deposit <amount>` | `arkoviaeconomy.bank` | Move gameplay funds from Wallet to Bank |
| `/bank withdraw <amount>` | `arkoviaeconomy.bank` | Move gameplay funds from Bank to Wallet |
| `/econhistory [count]` | `arkoviaeconomy.use` | Show recent economy ledger entries |
| `/treasury` | `arkoviaeconomy.treasury.view` | Show treasury balance and synchronization information |

`/bank deposit` and `/bank withdraw` are internal Terraria economy operations. They are **not blockchain deposits or withdrawals**.

Authorized administrators with audit permission may also inspect another TShock accounts balance using the supported administrative balance lookup.

---

## ⛓️ Player Blockchain Commands

The `/arkos` command family manages the players Arkovia blockchain identity.

```text
/arkos balance
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
```

### `/arkos balance`

Queries the real on-chain balance of the players linked Arkovia wallet.

### `/arkos wallet create`

Creates an Arkovia blockchain wallet and links its public identity to the authenticated TShock account.

Wallet creation uses the protected recovery workflow for the private recovery secret.

### `/arkos wallet address`

Displays the players public Arkovia address and account ID.

Public blockchain addresses are safe to share. Recovery secrets and private keys are not.

### `/arkos wallet status`

Shows whether an Arkovia wallet has been created and displays its public wallet information.

### `/arkos wallet recovery`

Requests a new secure recovery claim when the servers wallet-recovery infrastructure is available.

This command should never require the player to type the wallets existing recovery secret into Terraria chat.

---

## 🛡️ Administrator Commands

| Command | Permission | Description |
|---|---|---|
| `/eco reload` | `arkoviaeconomy.admin.config` | Reload economy configuration |
| `/eco sync` | `arkoviaeconomy.admin.treasury` | Run an immediate Arkovia funding synchronization |
| `/eco give <user> <amount> <reason>` | `arkoviaeconomy.admin.adjust` | Create an audited positive administrative adjustment |
| `/eco take <user> <amount> <reason>` | `arkoviaeconomy.admin.adjust` | Create an audited negative adjustment without allowing a negative balance |
| `/eco freeze <user>` | `arkoviaeconomy.admin` | Freeze an economy account |
| `/eco unfreeze <user>` | `arkoviaeconomy.admin` | Unfreeze an economy account |
| `/eco reward <user> <amount> <reason>` | `arkoviaeconomy.admin.treasury` | Pay a manual reward from the actual Terraria Treasury |

Administrative adjustments are recorded through the economy ledger so operators can audit changes later.

---

## 🔑 Permission Nodes

Current permission nodes include:

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

Example player/trusted-group permissions:

```text
/group addperm trusted arkoviaeconomy.use,arkoviaeconomy.pay,arkoviaeconomy.bank,arkoviaeconomy.wallet
```

Example administrative permissions:

```text
/group addperm admin arkoviaeconomy.admin,arkoviaeconomy.admin.adjust,arkoviaeconomy.admin.treasury,arkoviaeconomy.admin.config,arkoviaeconomy.admin.audit
```

Server owners may instead grant the plugin wildcard permission where appropriate for their TShock permission structure.

---

## 🚀 Installation

### Requirements

The current development environment uses:

```text
Terraria Server: 1.4.5.8
TShock / TSAPI: 6.1.x compatible build
.NET 9
```

Terraria and TShock compatibility changes over time. Always verify that the TShock build you are using supports your Terraria server version.

An Arkovia node is required only for blockchain-connected functionality such as treasury synchronization, blockchain wallet creation, and on-chain balance lookup.

The off-chain Terraria economy should be treated separately from blockchain availability.

### Option A — Install the Included DLL

This repository includes a compiled plugin at:

```text
release/ArkoviaEconomy.dll
```

Copy the DLL into the TShock server plugin directory:

```text
<TShock Server>/ServerPlugins/ArkoviaEconomy.dll
```

Then restart the TShock server.

Check the server console during startup for Arkovia Economy initialization messages or errors.

### Option B — Build from Source

Clone the repository:

```bash
git clone https://github.com/mycreationhaven/Arkos-Economy-Terraria-Plugin.git
cd Arkos-Economy-Terraria-Plugin
```

Restore and build:

```bash
dotnet restore
dotnet build -c Release
```

Normal build output:

```text
bin/Release/net9.0/ArkoviaEconomy.dll
```

Copy that DLL into the TShock `ServerPlugins` directory and restart TShock.

---

## 📦 Compiled Release

The `release/` directory contains the precompiled plugin for operators who do not want to build the project themselves.

The source code used to build the plugin is included in the repository so operators can inspect and compile it independently.

For security-sensitive or production deployments, building from reviewed source is encouraged.

---

## ⚙️ First Startup

On startup, Arkovia Economy initializes its configuration and economy storage under the TShock environment.

A typical configuration path is:

```text
tshock/ArkoviaEconomy/config.json
```

Runtime configuration files may contain deployment-specific information and should not automatically be committed to a public repository.

The repository provides a safe example configuration under:

```text
examples/config.example.json
```

Before upgrading a production installation, back up the economy database and configuration.

---

## 🔧 Basic Configuration

Important configuration areas include:

- currency name and symbol
- decimal precision
- transfer limits and fees
- banking behavior
- treasury solvency rules
- gameplay rewards
- death penalties
- PvP economy settings
- Arkovia node connectivity
- blockchain confirmation requirements
- treasury allocation percentage
- public API/privacy options

Use `/eco reload` after supported configuration changes when appropriate, or restart the server.

For production systems, validate configuration changes on a test server before applying them to a live economy.

---

## 🌐 Arkovia Node Configuration

A local Arkovia node can be configured with a URL such as:

```json
{
  "Arkovia": {
    "Enabled": true,
    "NodeUrl": "http://127.0.0.1:4876/nxt",
    "CommunityDevelopmentAccount": "ARK-KVFL-C6EE-2UD2-CSJ8Q",
    "ExpectedLedgerEventType": "BLOCK_GENERATED",
    "MinimumConfirmations": 10,
    "PollSeconds": 60,
    "LedgerPageSize": 100,
    "GameAllocationPercent": 100.0,
    "FeeDistributionActivationHeight": 1500,
    "CreditOnlyPositiveLedgerChanges": true,
    "RequireNodeToBeLocalOrHttps": true
  }
}
```

Do not place blockchain secret phrases, private keys, forging credentials, wallet passwords, or signing-service secrets in this configuration.

When the node runs on the same machine as TShock, localhost access is preferred over exposing sensitive node APIs to the public internet.

---

## 🔍 Verifying Installation

After starting TShock, verify that:

1. Arkovia Economy loads without a fatal plugin error.
2. An authenticated test player can run `/balance`.
3. Wallet and Bank values display correctly.
4. `/econhistory` returns ledger information.
5. `/treasury` works for an appropriately permitted account.
6. Gameplay rewards behave according to configuration.
7. Death and PvP deductions never create negative Wallet balances.
8. Blockchain commands work only when the Arkovia integration is correctly configured.

Test economic changes with small values before opening a production server to players.

---

## 🪙 Native ARKOS Configuration

The native/default currency for this project is **ARKOS**.

A standard ARKOS configuration should use:

```json
{
  "CurrencyName": "ARKOS",
  "CurrencySymbol": "ARKOS",
  "Decimals": 8
}
```

ARKOS uses eight decimal places, allowing very small gameplay rewards while keeping stored balances in integer atomic units.

---

## Custom on-chain currency selection

Set top-level `CurrencyId` to the numeric Arkovia Monetary System currency ID, or leave it blank for native ARKOS. At startup the node validates the ID and supplies the currency name, code and blockchain decimals. Invalid IDs or unavailable validation stop startup before economy commands and funding are enabled.

See [currency setup and upgrade instructions](docs/CONFIGURATION.md#currency-selection-and-safe-upgrades). Off-chain `Decimals` stays at its existing scale; blockchain precision never rewrites stored balances. Changing an existing economy's currency requires explicit acceptance of relabeling its numeric balances.

Administrators with `arkoviaeconomy.admin.treasury` can use `/treasury add <amount>`, `/treasury take <amount>`, and `/treasury`. Adjustments affect the internal treasury only, are audited, and cannot overdraw it.

## 🎨 Customizing Native Currency Presentation

Arkovia Economy is designed so server operators can customize the currency presented inside Terraria.

For example, a project using a currency called **Star Coin** could configure:

```json
{
  "CurrencyName": "Star Coin",
  "CurrencySymbol": "STAR",
  "Decimals": 8
}
```

Gameplay messages could then appear as:

```text
You earned 0.00100000 STAR for killing Zombie.
```

and balances could be presented using the configured `STAR` symbol instead of `ARKOS`.

### What CurrencyName controls

`CurrencyName` is the human-readable name used to describe the economy currency.

Examples:

```text
ARKOS
Star Coin
Kingdom Credit
Adventure Token
```

### What CurrencySymbol controls

`CurrencySymbol` is the short ticker or symbol displayed with gameplay amounts.

Examples:

```text
ARKOS
STAR
KC
ATK
```

The configured symbol is also used by gameplay messages and other economy presentation.

---

## ⚠️ Renaming the Currency Does Not Create a Blockchain

Changing `CurrencyName` or `CurrencySymbol` changes the **Terraria economy presentation**.

It does **not** automatically:

- create a new cryptocurrency
- create a new blockchain network
- change Arkovia consensus rules
- change the blockchain account/address format
- create a new treasury account
- create blockchain liquidity or market value
- change the native asset returned by a connected node

For example, changing the gameplay symbol from `ARKOS` to `STAR` does not cause an Arkovia node holding ARKOS to suddenly hold a separate STAR blockchain asset.

The underlying blockchain configuration must match the currency the server operator intends to represent.

---

## 🧩 Custom Arkovia Project Checklist

If you operate your own Arkovia-based blockchain or compatible currency project, review each of these areas before connecting it to Terraria.

### 1. Currency presentation

Configure:

```text
CurrencyName
CurrencySymbol
Decimals
```

The current economy architecture is designed around eight-decimal atomic accounting. Changing decimal behavior should be tested carefully throughout the entire economy before production use.

### 2. Blockchain node

Set the node endpoint used by your deployment:

```json
"NodeUrl": "http://127.0.0.1:4876/nxt"
```

Use the actual endpoint for your own trusted Arkovia-compatible node.

Do not point a production economy at an unknown or untrusted public node without understanding the security implications.

### 3. Treasury/funding account

The default project monitors:

```text
ARK-KVFL-C6EE-2UD2-CSJ8Q
```

That is the Arkovia Community & Development account used by the native project configuration.

An independent Arkovia-based project must determine whether that account is appropriate for its network.

If your blockchain uses a different funding or treasury account, configure and validate the correct public account for your network.

### 4. Funding event behavior

The native Arkovia integration expects:

```text
BLOCK_GENERATED
```

ledger credits for the Community & Development funding mechanism.

If your blockchain changes consensus-level fee distribution or ledger event behavior, review the synchronizer before enabling automatic treasury funding.

### 5. Account/address format

Changing the Terraria currency symbol does not automatically change blockchain addresses.

The blockchain address format is determined by the connected blockchain/network implementation.

Do not assume a custom gameplay ticker implies a custom blockchain address prefix.

### 6. Wallet generation

Player wallet generation depends on the connected Arkovia-compatible node returning the expected account ID, account address, and public key behavior.

Test wallet generation on a non-production server before allowing players to create wallets.

### 7. Treasury economics

Decide how gameplay currency enters and leaves the economy.

Consider:

- treasury funding rate
- gameplay reward ranges
- server fees
- death penalties
- PvP redistribution
- future shops and services
- configured deposits and withdrawals
- reserve requirements

A custom ticker alone is not an economic policy.

### 8. Security

Never copy private blockchain credentials into the Terraria plugin simply because your custom network uses different accounts.

Keep signing infrastructure isolated from normal gameplay infrastructure.

---

## 💡 Example Custom Deployment

Imagine a server project called **Starlight Realm** running an Arkovia-based network with a currency named **STAR**.

Its gameplay configuration might begin with:

```json
{
  "CurrencyName": "Starlight Coin",
  "CurrencySymbol": "STAR",
  "Decimals": 8,
  "StartingBalance": 0.0,
  "Arkovia": {
    "Enabled": true,
    "NodeUrl": "http://127.0.0.1:4876/nxt",
    "CommunityDevelopmentAccount": "YOUR-PUBLIC-ACCOUNT-HERE",
    "ExpectedLedgerEventType": "BLOCK_GENERATED",
    "MinimumConfirmations": 10
  }
}
```

`YOUR-PUBLIC-ACCOUNT-HERE` is intentionally a placeholder.

Do not paste a secret phrase or private key there. The funding synchronizer requires a **public blockchain account**, not its private recovery credentials.

The operator would then validate that its blockchain implements the ledger behavior expected by the plugin before enabling automatic treasury synchronization.

---

## 🎯 Economy Balancing Starting Points

Every Terraria server has a different population, play style, treasury size, and desired progression speed.

The following values are useful starting ranges for testing rather than universal rules:

| Activity | Suggested ARKOS range |
|---|---:|
| Common enemy | 0.0001 - 0.001 |
| Strong / rare enemy | 0.005 - 0.05 |
| Early boss | 0.10 - 0.25 |
| Mid-game boss | 0.25 - 0.50 |
| End-game boss | 0.50 - 1.00 |
| Quest | 0.05 - 0.25 |
| Normal death | 25% of Wallet |
| PvP death | -0.01 |

Operators using a custom currency should scale these values to the economics of that currency rather than copying ARKOS values blindly.

---

## 🏰 Old Ones Army / DD2 and Other Events

DD2 now tracks genuine event-enemy contributions and pays a configured multiplayer pool after confirmed victory. The hook order handles Terraria's nested victory callback correctly. Additional completion pools cover Goblin Army, Frost Legion, Pirate Invasion, Martian Madness, Blood Moon, Solar Eclipse, Pumpkin Moon, and Frost Moon.

Pools are proportional to damage, conserve atomic units exactly, and settle all recipients and ledger entries in one database transaction. Completed events wait in a durable queue if funds or recipient limits prevent settlement. Active, unfinished encounter contributions are not recovered across server restarts. Normal NPC rewards remain separate.

See [event configuration and completion rules](docs/EVENT_REWARDS.md).

---

## 🧑‍💻 Developer API

Arkovia Economy includes an internal API surface intended to make future Terraria systems use the same audited economy instead of implementing separate balance logic.

Potential integrations include:

- shops
- player markets
- jobs
- quests
- event rewards
- minigames
- server services
- custom NPC systems
- web dashboards
- deposit and withdrawal services

Integrations should use the central economy service whenever possible so balance changes remain auditable and treasury rules are consistently enforced.

See:

```text
docs/API.md
```

---

## 🗃️ Database and Ledger

Arkovia Economy maintains its own economy data rather than treating chat commands as the source of truth.

The database tracks financial state and an auditable ledger of economy activity.

Examples of ledger activity include:

- player transfers
- Wallet / Bank movement
- gameplay rewards
- death penalties
- PvP redistribution
- treasury funding
- administrative adjustments
- manual treasury rewards

Blockchain wallet records store public wallet identity separately from ordinary gameplay balances.

Private recovery secrets must not be stored in ordinary economy database records.

See:

```text
docs/DATABASE.md
```

---

## 🔒 Security Principles

Cryptocurrency integration adds responsibilities that do not exist in a normal game-points plugin.

This project follows several important principles:

1. **Never store blockchain secret phrases in the normal TShock economy database.**
2. **Never log recovery secrets, private keys, or internal API credentials.**
3. **Never ask players to type blockchain secrets into Terraria chat.**
4. **Keep public wallet information separate from private recovery material.**
5. **Prefer localhost communication for blockchain and signing infrastructure on the same server.**
6. **Use public account/address information for balance lookups whenever possible.**
7. **Keep gameplay transactions off-chain unless blockchain settlement is actually required.**
8. **Do not commit runtime databases, recovery artifacts, API keys, private keys, secret phrases, worlds, TLS keys, or production configuration files.**
9. **Back up economy data before upgrades.**
10. **Test financial changes with small values before production deployment.**

Read the repository security documentation before enabling blockchain-wallet functionality:

```text
SECURITY.md
docs/SECURITY.md
```

---

## 🛣️ Roadmap

The requested event, deposit, withdrawal, starter-grant, and PIN features are implemented in this release candidate. Live game-server/node staging remains required before enabling blockchain spending.

Future work includes full reserve/liability reports, automatic deposit discovery, external-wallet ownership linking, forgotten-PIN recovery tooling, boss-specific pools, active-encounter restart recovery, and shop/market/job features. See [the current roadmap](docs/ROADMAP.md).

---

## 🗂️ Repository Layout

```text
Arkos-Economy-Terraria-Plugin/
├── Api/
│   └── ArkoviaEconomyApi.cs
├── Commands/
│   └── EconomyCommands.cs
├── Config/
│   ├── ConfigManager.cs
│   └── EconomyConfig.cs
├── Core/
│   └── EconomyService.cs
├── Database/
│   └── EconomyDatabase.cs
├── Gameplay/
│   └── GameplayEconomyHandler.cs
├── Integrations/
│   ├── ArkoviaFundingSynchronizer.cs
│   ├── ArkoviaNodeClient.cs
│   └── WalletClaimClient.cs
├── Models/
│   ├── EconomyModels.cs
│   └── WalletModels.cs
├── docs/
├── examples/
├── release/
│   ├── ArkoviaEconomy.dll
│   └── README.md
├── ArkoviaEconomy.csproj
├── ArkoviaEconomyPlugin.cs
├── Permissions.cs
├── CHANGELOG.md
├── LICENSE
├── README.md
├── SECURITY.md
└── VALIDATION.md
```

---

## 📚 Documentation

Additional documentation is available in the `docs/` directory:

| Document | Purpose |
|---|---|
| `docs/INSTALLATION.md` | Installation and deployment |
| `docs/CONFIGURATION.md` | Configuration reference |
| `docs/COMMANDS.md` | Commands and permissions |
| `docs/ARKOVIA_BLOCKCHAIN.md` | Arkovia blockchain integration |
| `docs/ECONOMY_DESIGN.md` | Economy architecture and design |
| `docs/DATABASE.md` | Database and ledger structure |
| `docs/API.md` | Developer integration API |
| `docs/SECURITY.md` | Detailed security guidance |
| `docs/ROADMAP.md` | Planned development |

---

## 🤝 Contributing

Contributions, testing, bug reports, documentation improvements, and security reviews are welcome.

When changing economy behavior:

- preserve integer atomic accounting
- preserve ledger auditability
- avoid negative balances unless explicitly designed and documented
- maintain TShock account identity safety
- do not expose blockchain secrets
- keep tenant/server-specific credentials out of source control
- test treasury effects
- test multiplayer edge cases
- test duplicate-event handling
- document whether a feature is implemented, experimental, or planned

For security-sensitive changes, review `SECURITY.md` before opening a public issue containing implementation details.

---

## 🧪 Development Philosophy

Arkovia Economy is built around a simple separation of concerns:

> **Terraria should remain fast enough to feel like a game, while blockchain settlement should remain deliberate enough to behave like money.**

That means ordinary monster kills, PvP activity, banking, shops, quests, and other high-frequency actions belong in the off-chain gameplay ledger.

Blockchain operations are reserved for actions where public ownership, settlement, deposits, withdrawals, or other on-chain properties are actually useful.

The result is intended to feel like a Terraria economy first, while still allowing players and server operators to connect that economy to the Arkovia ecosystem.

---

## 📜 License

This project is licensed under the **GNU General Public License v3.0 or later (GPL-3.0-or-later)**.

See the included `LICENSE` file for the complete license terms.

---

## 🌎 Project Goal

The long-term goal of Arkovia Economy is to provide an open, auditable bridge between Terraria gameplay economies and the Arkovia blockchain without forcing every sword swing, monster kill, or player transaction onto the blockchain.

Build worlds. Create economies. Let players own what actually needs to be on-chain.
