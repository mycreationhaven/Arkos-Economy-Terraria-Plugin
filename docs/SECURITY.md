# Security model

## Secrets that must never be stored in this plugin

- Arkovia secret phrases / seed phrases
- private keys
- wallet backup files
- forging credentials
- keystore passwords
- exchange API secrets
- GitHub tokens

The treasury synchronizer needs only the public account address and a read-only node API.

## Node transport

`RequireNodeToBeLocalOrHttps=true` permits HTTP only for loopback/localhost. Remote Arkovia nodes must use HTTPS. This does not make a public node trustworthy; operators should preferably run their own node.

## Financial invariants

- Money is integer atomic units.
- Negative transfers are rejected.
- Frozen accounts cannot transact.
- Transfers cannot spend more than wallet balance.
- Treasury rewards cannot exceed treasury balance.
- Confirmed blockchain funding is idempotent.
- Admin adjustments require an audit reason.
- External plugins should use the API instead of changing balance columns.

## Operational controls

Back up the TShock database before upgrades. Restrict admin economy permissions. Do not grant `arkoviaeconomy.admin.adjust` broadly. Review `/econhistory`, treasury state and database ledger during incident investigation.

## Withdrawal architecture

Do not add the treasury private key to TShock to support withdrawals. Use a separate bridge/signing service with withdrawal limits, replay protection, authentication, allowlists/risk controls if desired, and a hot-wallet amount intentionally much smaller than the main treasury.
