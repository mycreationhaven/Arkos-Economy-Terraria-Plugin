# Arkovia Economy — Security Model

Security is part of the economy design, especially where off-chain Terraria balances meet real Arkovia blockchain accounts.

---

## Security Boundaries

Arkovia Economy separates three major classes of information:

1. gameplay economy data
2. public blockchain identity and state
3. private blockchain recovery or signing material

These classes should not be treated as interchangeable.

---

## Gameplay Economy Data

Normal gameplay economy data includes:

- TShock user ID
- Wallet balance
- Bank balance
- transaction ledger entries
- treasury movements
- reward and penalty history
- freeze state
- public audit information

Gameplay balances are stored as integer atomic units.

They are not private blockchain keys and they are not themselves on-chain ARKOS balances.

---

## Public Blockchain Information

The plugin may store or query public blockchain information such as:

- Arkovia account ID
- Arkovia account address
- public key
- account creation metadata
- public account balance
- public ledger events

Public blockchain addresses are safe to use for balance lookup and account identification.

Public information must never be confused with authority to spend from the account.

---

## Private Recovery Material

Private blockchain recovery material requires a separate security boundary.

Examples include:

- secret phrases
- recovery secrets
- private keys
- wallet backup material
- signing credentials
- keystore passwords

These values must not be stored in the ordinary TShock economy database, normal gameplay records, ordinary configuration, or logs.

They must never be committed to the public repository.

---

## Player Wallet Generation

Player wallet creation is an exception to any simplistic rule that the plugin never handles a secret phrase.

During wallet creation, the plugin generates a cryptographically secure secret phrase and temporarily uses it with the configured trusted Arkovia node to derive the blockchain account.

The generated recovery material is then handled through the protected recovery workflow.

Only public wallet identity should be persisted in the normal economy database.

The generated secret must not be written to ordinary logs, gameplay history, or normal configuration.

---

## Never Send Secrets Through Terraria Chat

Players and administrators must never paste a real:

- Arkovia secret phrase
- recovery secret
- private key
- signing key
- wallet password

into ordinary Terraria chat or a normal TShock command.

Commands and chat may be logged, displayed, proxied, or captured by unrelated systems.

---

## Protected Recovery Workflow

Recovery material should be stored only where required by the dedicated protected recovery process.

Supported deployments may use a separate localhost wallet-claim service to present recovery information to the player.

Recovery files should use restrictive operating-system permissions and should not be placed under web roots, source trees, public repositories, or ordinary game-data directories.

Recovery-service credentials must remain outside Git and outside normal public configuration examples.

---

## Treasury Synchronizer Security

The Community & Development treasury synchronizer is read-only.

It needs only:

- the public funding account
- access to an appropriate Arkovia node API

It does not need the Community & Development accounts secret phrase, private key, forging credentials, or wallet password.

Do not add those credentials merely to support ledger synchronization.

---

## Node Transport

`RequireNodeToBeLocalOrHttps=true` permits ordinary HTTP only for loopback/localhost connections.

Remote Arkovia nodes should use HTTPS.

HTTPS protects transport but does not make an untrusted node trustworthy.

Operators should prefer a node they control for security-sensitive blockchain operations.

---

## Public Node Exposure

A blockchain node used locally by the plugin should expose only the interfaces intentionally required by the deployment.

Do not assume that a reverse proxy makes administrative or sensitive node API methods safe for public access.

Review firewall and reverse-proxy rules before enabling production blockchain-connected features.

---

## Financial Invariants

The economy should preserve these rules:

- currency is stored in integer atomic units
- negative transfers are rejected
- balances cannot be spent below available funds
- frozen accounts cannot perform restricted transactions
- Bank funds remain protected from normal death and PvP Wallet penalties
- treasury-backed rewards cannot exceed available treasury funds when solvency enforcement is enabled
- confirmed blockchain funding must not be credited twice
- administrator adjustments require appropriate permission and an audit reason
- external integrations should use the economy API instead of directly modifying balance columns

---

## Death and PvP Safety

Gameplay penalties operate on the off-chain Wallet.

They must not directly debit:

- protected Bank savings
- an Arkovia blockchain wallet
- an external wallet

Losses should be clamped to available Wallet funds so gameplay penalties cannot create debt.

PvP winner and treasury percentages must total 100 percent.

---

## Permissions

Use least privilege for TShock economy permissions.

Administrative capabilities such as balance adjustment, treasury management, configuration, and auditing should be restricted to trusted groups.

Do not grant broad administrative wildcard permissions to ordinary players.

Player wallet commands should still require an authenticated TShock account.

---

## Database and Ledger Integrity

All currency movement should pass through the ledger-backed economy service/API rather than direct balance-column modification.

Important financial mutations should record:

- who or what initiated the action
- amount
- source and destination where applicable
- reason/event type
- timestamp
- reference or idempotency information where applicable

Database backups should be taken before plugin upgrades or schema changes.

---

## API Privacy

The public read API should expose only information intentionally configured for public access.

Player balance exposure should remain disabled unless the operator intentionally enables it.

Never expose recovery material, private keys, internal service credentials, or privileged administrative data through the public API.

---

## Logging

Logs should contain enough information to diagnose economy behavior without exposing credentials.

Never log:

- secret phrases
- recovery secrets
- private keys
- wallet-claim API credentials
- signing-service credentials
- authentication tokens

Errors returned to ordinary players should also avoid leaking internal filesystem paths, credentials, or sensitive service details.

---

## Deposits, withdrawals, grants, and PINs

Implemented transfer flows are disabled until configured. See [the complete deployment and security boundaries](BLOCKCHAIN_SETUP.md).

Deposits verify the selected currency, sender's linked wallet, reserve destination, amount, full hash, confirmations, and absence of phasing. A unique operation record and wallet credit commit together. Unconfirmed transactions are not credited.

Withdrawals require a logged-in authorized TShock user and PIN confirmation on the HTTPS portal. The separate signer holds the reserve secret; TShock receives signed bytes, independently validates them against the node, and stores them with a wallet hold before any broadcast. Retries use exactly the same signed bytes. Duplicate outgoing full hashes cannot reserve funds twice. The reserve pays the actual native network fee, subject to caps and pending-payment coverage.

A timeout never refunds a potentially broadcast transaction. Treasury administrators can explicitly reconcile only a transaction absent from a fresh trusted node after its deadline plus a one-hour safety window. Known or uncertain payments retain their hold. Deep reorganizations beyond the confirmation policy are not automatically reversed.

Starter grants use separate eligibility permissions, one durable record per TShock user ID, a daily cap, and reserve limits. Those controls do not prove unique human identity. Do not grant starter eligibility automatically to disposable accounts.

PINs never enter Terraria command text. The HTTPS portal exchanges account-bound, six-digit one-time codes for 256-bit expiring bearer sessions. Codes expire with their original session deadline, are invalidated after five wrong guesses, and have a server-wide 60-redemption-attempts/minute limit. Bearer tokens are held only in browser memory, never in URLs or browser storage. The portal uses origin validation, a restrictive content security policy, and permission rechecks. PIN storage uses a random salt, PBKDF2-SHA256 with 600,000 iterations, constant-time comparison, and persistent five-attempt/15-minute lockouts. Changing a PIN requires the current PIN; there is no unauthenticated reset endpoint. Proxy administrators must not log Authorization headers or request bodies.

New event pools and blockchain settlements are atomic. Legacy gameplay/bank/PvP operations retain their existing implementations and are not covered by a claim that every historical economy operation is now atomic.

---

## Source Control Exclusions

Never commit:

- production `config.json` files containing deployment-specific data
- TShock databases
- Terraria worlds
- wallet recovery artifacts
- wallet-claim API keys
- secret phrases
- private keys
- signing credentials
- TLS private keys
- server logs containing sensitive information
- temporary source backups

Use the repository `.gitignore` as an additional safety layer, not as the only security control.

---

## Incident Response

If suspicious economy activity occurs:

1. restrict affected economy permissions if necessary
2. preserve relevant logs and database backups
3. inspect `/econhistory` and treasury state
4. review the ledger rather than editing balances manually
5. inspect blockchain transactions when on-chain activity is involved
6. rotate any credential that may have been exposed
7. document corrective adjustments with an audit reason

Never publish sensitive recovery material while asking for troubleshooting help.

---

## Core Principle

Public blockchain information may be shared and queried.

Private recovery and signing authority must remain isolated, minimized, and protected.
