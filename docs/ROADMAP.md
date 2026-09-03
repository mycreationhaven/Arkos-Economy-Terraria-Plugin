# Arkovia Economy roadmap

## Implemented in 1.2.0 release candidate

- DD2 enemy participation tracking and corrected nested victory callback handling.
- Atomic, contribution-weighted DD2 multiplayer completion pools.
- Goblin Army, Frost Legion, Pirate Invasion, Martian Madness, Blood Moon, Solar Eclipse, Pumpkin Moon, and Frost Moon completion rewards.
- Confirmed transaction-hash deposits with replay protection.
- PIN-authorized withdrawals with operator-paid actual network fees, a separate local signer, durable holds, confirmation tracking, and guarded expiry reconciliation.
- Optional one-time starter grants with eligibility permissions, daily cap, reserve floor, and durable states.
- Private HTTPS PIN workflow, salted PBKDF2 hashing, persistent lockouts, and expiring sessions.

These implementations require staging validation. Transfer-related features are disabled by default until the reserve, signer, portal, and permissions are configured. See [setup](BLOCKCHAIN_SETUP.md), [event rewards](EVENT_REWARDS.md), and [validation](../VALIDATION.md).

## Remaining development

- Full reserve/liability coverage reporting and operational dashboards.
- Automatic deposit discovery and player status notifications.
- Explicit externally owned wallet linking through signed ownership challenges.
- Operator-reviewed forgotten-PIN recovery UI.
- Deep-reorganization incident/reconciliation tooling.
- Persisting active event contribution across unplanned server restarts.
- Boss-specific shared pools, progression classification, and segmented-boss rules.
- Additional event-specific enemy classifications and final-wave reward policies.
- Stronger identity-based anti-farming beyond permissions and per-account limits.
- Broader atomicity migration for legacy gameplay/bank/PvP operations.
- Shop/market item escrow, jobs, contracts, businesses, and player privacy controls.

Every money movement should use authoritative ledger-backed logic. Game rewards remain off-chain; signing authority stays outside the game server.
