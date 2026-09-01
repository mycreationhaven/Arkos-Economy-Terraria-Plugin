# Configuration

The file is created at `tshock/ArkoviaEconomy/config.json`.

## Currency

`CurrencyName` and `CurrencySymbol` control display labels. `Decimals` controls the internal atomic unit. Arkovia uses 8 decimals, so the default `Decimals: 8` means 1 ARK = 100,000,000 atomic units.

`StartingBalance` defaults to zero. This is intentional: if you want a fully treasury-backed economy, do not create player money at account creation.

`MinimumTransfer` and `MaximumPlayerBalance` limit player payments and account growth.

## Fees

`PlayerTransferFeePercent`, `Banking.DepositFeePercent`, and `Banking.WithdrawalFeePercent` can charge economic fees. When `ReturnServerFeesToTreasury` is true, those fees recycle into the internal Terraria Treasury.

## Arkovia

- `Enabled`: enables automatic funding sync.
- `NodeUrl`: Arkovia Nxt-style HTTP API endpoint. Default local endpoint is `http://127.0.0.1:4876/nxt`.
- `CommunityDevelopmentAccount`: default `ARK-KVFL-C6EE-2UD2-CSJ8Q`.
- `ExpectedLedgerEventType`: must remain `BLOCK_GENERATED` for the current 5% mechanism.
- `MinimumConfirmations`: minimum blocks before a ledger credit is accepted. Default 10.
- `PollSeconds`: polling interval, minimum 15 seconds.
- `LedgerPageSize`: recent ledger entries requested from the node.
- `GameAllocationPercent`: percentage of each confirmed 5% credit allocated to Terraria. `100` means all newly received 5% fee credits become available to the game treasury.
- `FeeDistributionActivationHeight`: current Arkovia source activates 80/15/5 at height 1500.
- `CreditOnlyPositiveLedgerChanges`: prevents debits from being interpreted as funding.
- `RequireNodeToBeLocalOrHttps`: blocks plain HTTP access to remote nodes. Localhost HTTP remains allowed.

## Recommended production settings

Run an Arkovia node on the same machine or private network as the Terraria server when possible. If using a remote node, terminate with HTTPS. Keep `MinimumConfirmations` at 10 or higher for conservative funding finality.

Do not add private keys or secret phrases to the configuration. They are neither needed nor supported.
