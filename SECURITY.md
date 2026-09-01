# Security Policy

Arkovia Economy separates normal Terraria economy data from blockchain secrets.

## Never commit or publish

- Arkovia secret phrases
- blockchain private keys
- wallet recovery secrets
- API keys
- signing-service credentials
- production database files
- Terraria account databases
- private server configuration
- TLS private keys

## Wallet security

Public blockchain information such as account ID, account address, and public key may be stored by the plugin.

Generated blockchain secret phrases must be treated as private recovery material and must never be stored in ordinary TShock economy tables, logs, configuration, or normal Terraria chat commands.

Players should never paste a real blockchain secret phrase into Terraria chat.

## Node security

Production node operators should expose only the network APIs required by their deployment. Administrative or sensitive node APIs should remain private whenever possible.

## Reporting security issues

Do not post secret material or sensitive exploitation details in a public GitHub issue.
