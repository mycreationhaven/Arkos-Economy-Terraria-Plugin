# Arkovia blockchain integration

The plugin keeps frequent gameplay Wallet/Bank movements off-chain. Native ARKOS and selected Monetary System currencies are supported through the top-level `CurrencyId` setting. Node-validated blockchain precision stays independent from the existing off-chain storage scale.

## Implemented capabilities

- Read-only Community & Development treasury funding synchronization.
- Linked player wallet generation and the existing protected recovery-claim workflow.
- Public on-chain balance lookup.
- Confirmed transaction-hash deposits into the gameplay Wallet, with atomic replay protection.
- PIN-authorized withdrawals, real fee quotes, durable holds, exact-byte retries, and confirmation tracking.
- Optional one-time starter blockchain grants with permission eligibility, reserve limits, and a daily cap.
- A separate loopback signing service that never broadcasts and does not have TShock database access.
- An HTTPS-facing PIN portal whose PIN values never go through Terraria commands.

Deposits, withdrawals, and grants require deployment configuration; they are disabled by default. See [complete setup and player workflow](BLOCKCHAIN_SETUP.md), [configuration](CONFIGURATION.md), and [validation status](../VALIDATION.md).

## Commands and accounting

`/bank deposit` and `/bank withdraw` remain internal Wallet/Bank movements. Blockchain deposits use `/arkos deposit <fullHash>`. Withdrawals use `/arkos security` to review and confirm on the private portal. `/arkos transfers` reports pending and confirmed operations.

Network fees are paid by the reserve in native ARKOS. The plugin independently parses signed bytes through the trusted node and verifies the sender, recipient, currency, amount, signature, conditions, and fee cap before holding gameplay funds. Signed bytes are persisted before the first submission. Confirmation finalizes a hold without another deduction.

The transfer reserve must be distinct from the automatic gameplay funding account. Reserve checks include pending amounts/fees and a configured floor. Comprehensive reserve-to-liability reporting remains future work.

## Existing funding synchronizer

The native source monitors the configured `BLOCK_GENERATED` ledger event. Custom-currency default funding uses `CURRENCY_TRANSFER` and selected currency holdings. The existing empty-ledger balance-growth fallback retains its original limitation: it does not provide the ledger path's confirmation-depth guarantee. It is separate from player deposit verification, which always requires a specific confirmed transaction.

## Security and deployment boundaries

Only public wallet identity is stored in the normal player-wallet table. The existing wallet-creation flow briefly handles a generated player secret and stores it only in its protected recovery package. The reserve secret belongs exclusively to the signer environment. PINs are salted, iterated hashes; portal bearer tokens expire and are stored only as hashes.

Never paste secrets or PINs into Terraria chat. Never expose signer/node administration endpoints through the public portal. Back up operation and ledger records together. Deep chain reorganizations, lost PIN recovery, and changes of reserve/network require operator review; see the [security model](SECURITY.md).
