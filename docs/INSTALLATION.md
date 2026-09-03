# Arkovia Economy — Installation & Operations

This guide covers installation, first startup, permissions, initial testing, upgrades, and optional Arkovia blockchain integration.

---

## Requirements

- Terraria server compatible with the TShock build you are using
- TShock 6.1.x-compatible environment
- .NET 9 runtime for the current plugin build
- write access to the TShock `ServerPlugins` directory
- TShock user accounts for players using account-based economy features

For building from source, install the .NET 9 SDK.

An Arkovia node is required only for blockchain-connected features.

---

## Download / Compiled Plugin

The repository includes a compiled plugin at:

```text
release/ArkoviaEconomy.dll
```

You may use the compiled DLL or build the project from source.

---

## Build From Source

From the repository root:

```bash
dotnet build -c Release
```

The compiled plugin is normally produced at:

```text
bin/Release/net9.0/ArkoviaEconomy.dll
```

---

## Install the Plugin

Stop the Terraria/TShock server before replacing plugin binaries.

Copy:

```text
ArkoviaEconomy.dll
```

into the TShock:

```text
ServerPlugins/
```

directory.

Start the server normally.

---

## First Startup

On first startup, Arkovia Economy creates its configuration under the TShock save path:

```text
tshock/ArkoviaEconomy/config.json
```

The plugin also creates or verifies its required database tables during startup.

After the first startup:

1. stop the server normally
2. back up the generated configuration
3. review the configuration carefully
4. restart the server

Do not place blockchain private keys, recovery secrets, wallet passwords, or signing credentials in the normal plugin configuration.

---

## Basic Configuration

The native/default currency is ARKOS.

Important configuration areas include:

```text
CurrencyName
CurrencySymbol
Decimals
Banking
Rewards
GameplayEconomy
Arkovia
Api
```

See:

```text
docs/CONFIGURATION.md
examples/config.example.json
```

for configuration details and a public-safe example.

---

## TShock Accounts

Arkovia Economy uses authenticated TShock accounts as the stable identity for player economy records.

Players should register and log into TShock before using account-based commands.

The plugin should not use display names alone as authoritative financial identity.

---

## Permissions

Grant permissions using normal TShock group management.

Common player capabilities include:

```text
arkoviaeconomy.use
arkoviaeconomy.pay
arkoviaeconomy.bank
arkoviaeconomy.wallet
```

Administrative permissions should be granted only to trusted staff.

See `docs/COMMANDS.md` for the complete command and permission reference.

---

## First Off-Chain Test

Before testing blockchain functionality, verify the internal gameplay economy.

Recommended checks:

1. log into a TShock account
2. run `/balance`
3. inspect `/treasury` with appropriate permission
4. issue a small test reward using an authorized administrator account
5. verify the player Wallet increases
6. verify the treasury decreases by the same funded amount
7. inspect `/econhistory`

If gameplay rewards are enabled, test a common enemy with deliberately small reward values.

---

## Death and PvP Testing

Death and PvP economy behavior should be tested with small Wallet balances before production use.

Verify that:

- normal death affects only the gameplay Wallet
- Bank funds remain protected
- balances never become negative
- death cooldown behavior works as configured
- PvP loss is based only on the amount actually available
- PvP winner and treasury percentages total 100 percent
- transaction history records the expected movement

---

## Floating Currency Feedback

The current source includes native Terraria floating/combat-text feedback for currency gains and losses.

Positive changes appear as a green signed number and negative changes as a red signed number.

This uses Terraria network behavior and does not require a separate client mod.

Server operators should still verify visual behavior against their exact Terraria/TShock build before production deployment.

---

## Optional Arkovia Node Integration

Blockchain-connected features require access to a compatible Arkovia node.

A typical same-server configuration is:

```text
http://127.0.0.1:4876/nxt
```

Localhost is preferred when TShock and the Arkovia node run on the same trusted machine.

Remote node URLs should use HTTPS and should be treated as external trust dependencies.

---

## Treasury Synchronizer Test

For the native ARKOS deployment, the configured Community & Development account is:

```text
ARK-KVFL-C6EE-2UD2-CSJ8Q
```

The funding synchronizer expects eligible account-ledger events such as:

```text
BLOCK_GENERATED
```

After the configured confirmation count is reached, verify that an eligible funding record is processed once and that the Terraria treasury receives the configured allocation.

The treasury synchronizer is read-only and does not require the funding accounts private recovery credentials.

---

## Player Blockchain Wallet Test

Blockchain wallet creation should be tested only after the node and recovery workflow are configured correctly.

Useful commands include:

```text
/arkos wallet create
/arkos wallet address
/arkos wallet status
/arkos wallet recovery
/arkos balance
```

Never paste a real recovery secret or private key into ordinary Terraria chat or command input.

Wallet recovery material must remain outside the normal gameplay economy database and outside the public repository.

---

## Features That Do Not Require Blockchain Transactions

The following normal gameplay features remain off-chain:

- player Wallet balances
- Bank balances
- player payments
- NPC rewards
- death penalties
- PvP economy transfers
- administrator adjustments
- treasury-backed gameplay rewards
- transaction history

This keeps frequent gameplay actions fast and avoids creating blockchain transactions for every Terraria event.

---

## Blockchain Features Not Yet Enabled

Do not assume the following are currently available:

- gameplay-to-blockchain withdrawals
- blockchain-to-gameplay deposits
- automatic outgoing transaction signing
- starter-wallet ARKOS grants
- player security PIN authorization
- completed DD2 event payout settlement

These remain future or in-development capabilities.

---

## DD2 / Old Ones Army

The current source contains DD2 event tracking infrastructure.

Completed DD2 reward settlement is intentionally not enabled yet because multiplayer event payout should use a safe atomic treasury operation.

Do not advertise DD2 completion rewards as active until that settlement path is implemented and tested.

---

## Custom Currency Deployments

Operators can change the Terraria-facing currency name and symbol.

For example, another project may replace the ARKOS presentation with its own configured currency identity.

Changing the display configuration does not automatically create a new blockchain or change the Arkovia account format.

Custom deployments should verify their own node, funding account, economics, wallet behavior, reserve model, and security assumptions.

The current blockchain command namespace remains `/arkos` unless the source is customized.

---

## Upgrading

Before upgrading:

1. stop the server normally
2. back up the TShock database
3. back up `tshock/ArkoviaEconomy/`
4. preserve any protected wallet-recovery material according to your deployment policy
5. replace the plugin DLL
6. start the server
7. review startup logs
8. verify configuration compatibility
9. test `/balance`, `/treasury`, and `/econhistory`
10. perform a small gameplay transaction before reopening normal economy activity

---

## Troubleshooting Checklist

If the plugin does not behave as expected, check:

- Terraria/TShock version compatibility
- .NET runtime availability
- plugin DLL location
- file permissions
- TShock account login state
- TShock permission nodes
- JSON configuration validity
- database access
- Arkovia node reachability when blockchain features are enabled
- recovery service availability when recovery claims are enabled
- server logs for plugin errors

Never troubleshoot by posting private recovery material or production credentials publicly.

## 1.2.0 release candidate

Replace the DLL while the server is stopped after backing up the database. The new ArkoviaOperations table is added without rescaling balances. Configure off-chain event pools through EventRewards. Blockchain transfers require a separate signer process and HTTPS PIN portal; follow [BLOCKCHAIN_SETUP.md](BLOCKCHAIN_SETUP.md). Test staged deposits, withdrawals, interrupted submission recovery, DD2, and the other events before enabling them for players.
