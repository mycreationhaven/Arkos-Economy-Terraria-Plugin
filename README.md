# Arkovia Economy for TShock

Arkovia Economy is a treasury-backed economy framework for Terraria servers running TShock. It gives every authenticated TShock account an auditable ARK wallet and optional bank balance, supports player transfers and treasury-funded rewards, and can automatically credit the in-game treasury from the **5% Arkovia blockchain fee-distribution account**.

## Core idea

Arkovia block fees are distributed by the blockchain as **80% to the block forger, 15% to the Arkovia Network Treasury, and 5% to the Community & Development account**. In the current Arkovia source supplied with this project, the 5% account is:

`ARK-KVFL-C6EE-2UD2-CSJ8Q`

The Terraria plugin watches confirmed `BLOCK_GENERATED` ledger credits for that public account. Confirmed credits are recorded once in `ArkoviaEconomyFunding`, allocated according to `GameAllocationPercent`, and credited to the internal `Terraria Treasury`. The plugin never needs or accepts the account's private key or secret phrase.

```text
Arkovia transactions
       │
       ▼
Blockchain transaction fees
       │
       ├── 80% block forgers
       ├── 15% Network Treasury
       └──  5% Community & Development account
                        │
                        ▼ read-only account ledger
               Arkovia Economy synchronizer
                        │
                        ▼
                 Terraria Treasury
                        │
             ┌──────────┼───────────┐
             ▼          ▼           ▼
          rewards     events      plugins
             │
             ▼
           players
             │
       fees / services
             │
             └──────────────► Treasury
```

## Included in v1.0

- TShock-account-backed economy accounts
- Wallet and bank balances
- Immutable transaction ledger
- `/balance`, `/pay`, `/bank`, `/econhistory`, `/treasury`
- Admin grant/take/freeze/reward tooling
- Treasury-backed reward path
- Arkovia 5% account-ledger synchronizer
- Confirmation requirement and duplicate-credit protection
- Configurable game allocation percentage
- Configurable transfer/banking fees that can recycle into the treasury
- In-process API for other TShock plugins
- Database tables for shop and market modules
- Security-first node URL validation
- Full operator, API, command, configuration, blockchain, and security documentation

## Requirements

- TShock 6.1.x / TSAPI 6.1.x
- .NET 9 SDK to build
- Terraria/TShock server compatible with TShock 6.1.x
- An accessible Arkovia node for automatic treasury funding

TShock currently targets .NET 9 and loads plugins from `ServerPlugins`.

## Build

```bash
dotnet restore
dotnet build -c Release
```

Copy `bin/Release/net9.0/ArkoviaEconomy.dll` into the server's `ServerPlugins` directory and restart TShock.

On first startup the plugin creates:

```text
tshock/ArkoviaEconomy/config.json
```

## Important Arkovia node requirement

The synchronizer uses `getAccountLedger`, because the 5% fee credit is an internal `BLOCK_GENERATED` balance event rather than a normal payment transaction. Your supplied Arkovia source defaults to `nxt.ledgerAccounts=*`, so account ledger tracking is enabled for all accounts. If this is changed on production nodes, ensure the 5% treasury account remains tracked.

## Permissions

See [docs/COMMANDS.md](docs/COMMANDS.md).

## Developer API

Other TShock plugins can use `ArkoviaEconomy.Api.ArkoviaEconomyApi.Instance` to query/create player economy accounts, inspect the treasury, make account transfers, and issue treasury-backed rewards. See [docs/API.md](docs/API.md).

## Security

**Never put an Arkovia secret phrase, private key, wallet file, or forging credential in this plugin's configuration.** The integration is deliberately read-only. See [docs/SECURITY.md](docs/SECURITY.md).

## Documentation

- [Installation & Operations](docs/INSTALLATION.md)
- [Commands & Permissions](docs/COMMANDS.md)
- [Configuration](docs/CONFIGURATION.md)
- [Plugin API](docs/API.md)
- [Arkovia Blockchain Integration](docs/ARKOVIA_BLOCKCHAIN.md)
- [Database & Ledger](docs/DATABASE.md)
- [Economy Design](docs/ECONOMY_DESIGN.md)
- [Security](docs/SECURITY.md)
- [Roadmap / Extension Modules](docs/ROADMAP.md)

## License

The plugin is marked GPL-3.0-or-later to remain compatible with TShock's licensing. Arkovia blockchain source remains separately licensed under the terms included with the Arkovia/Nxt distribution. This plugin communicates with Arkovia over HTTP and does not embed the blockchain source.
