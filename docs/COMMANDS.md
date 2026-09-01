# Commands and permissions

## Player commands

| Command | Permission | Description |
|---|---|---|
| `/balance`, `/bal`, `/money` | `arkoviaeconomy.use` | Shows wallet, bank and combined balance. Admin-audit users may specify a TShock account name. |
| `/pay <account> <amount>` | `arkoviaeconomy.pay` | Transfers ARK from one authenticated TShock account to another. Offline recipients are supported. |
| `/bank balance` | `arkoviaeconomy.bank` | Shows wallet and bank balances. |
| `/bank deposit <amount>` | `arkoviaeconomy.bank` | Moves funds from wallet to bank, applying configured fee. |
| `/bank withdraw <amount>` | `arkoviaeconomy.bank` | Moves funds from bank to wallet, applying configured fee. |
| `/econhistory [count]` | `arkoviaeconomy.use` | Shows the most recent 1-20 ledger entries involving the caller. |
| `/treasury` | `arkoviaeconomy.treasury.view` | Shows internal treasury balance and blockchain sync status. |

Players must be authenticated to a TShock account before using financial commands. Character names are not the financial identity; TShock User ID is.

## Admin commands

| Command | Permission | Description |
|---|---|---|
| `/eco reload` | `arkoviaeconomy.admin.config` | Reload configuration from disk. |
| `/eco sync` | `arkoviaeconomy.admin.treasury` | Run an immediate Arkovia ledger synchronization. |
| `/eco give <user> <amount> <reason>` | `arkoviaeconomy.admin.adjust` | Manual positive adjustment with immutable audit entry. |
| `/eco take <user> <amount> <reason>` | `arkoviaeconomy.admin.adjust` | Manual negative adjustment; cannot drive a balance below zero. |
| `/eco freeze <user>` | `arkoviaeconomy.admin` | Freeze an economy account. |
| `/eco unfreeze <user>` | `arkoviaeconomy.admin` | Unfreeze an economy account. |
| `/eco reward <user> <amount> <reason>` | `arkoviaeconomy.admin.treasury` | Pays a player from the actual Terraria Treasury rather than minting funds. |

## Permission nodes

- `arkoviaeconomy.use`
- `arkoviaeconomy.pay`
- `arkoviaeconomy.bank`
- `arkoviaeconomy.shop`
- `arkoviaeconomy.market`
- `arkoviaeconomy.jobs`
- `arkoviaeconomy.treasury.view`
- `arkoviaeconomy.admin`
- `arkoviaeconomy.admin.adjust`
- `arkoviaeconomy.admin.treasury`
- `arkoviaeconomy.admin.config`
- `arkoviaeconomy.admin.audit`

Example TShock group configuration:

```text
/group addperm trusted arkoviaeconomy.use,arkoviaeconomy.pay,arkoviaeconomy.bank
/group addperm admin arkoviaeconomy.admin,arkoviaeconomy.admin.adjust,arkoviaeconomy.admin.treasury,arkoviaeconomy.admin.config,arkoviaeconomy.admin.audit
```
