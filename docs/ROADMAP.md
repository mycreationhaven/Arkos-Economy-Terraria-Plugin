# Arkovia Economy — Roadmap

This roadmap separates completed foundations from active development and longer-term extensions.

---

## Available Today

Current implemented foundations include:

- TShock account-backed economy identity
- off-chain Wallet and Bank balances
- eight-decimal atomic accounting
- player payments
- transaction history
- administrator adjustments and freeze controls
- treasury-backed rewards
- Community & Development ledger synchronization
- NPC gameplay rewards
- configurable gameplay reward ranges
- normal death Wallet penalties
- PvP Wallet penalties and winner/treasury distribution
- native floating currency feedback in the current source
- player-created Arkovia blockchain wallets
- public wallet identity persistence
- protected recovery workflow
- optional local recovery-claim integration
- on-chain public balance lookup
- DD2 / Old Ones Army tracking infrastructure
- configurable currency presentation
- in-process economy API

---

## Immediate Development Priorities

The next priorities should focus on correctness, security, and multiplayer-safe settlement.

### Atomic Multiplayer Economy Operations

Multi-recipient payouts should use one authoritative transactional operation rather than a sequence of independent balance mutations.

This is especially important for:

- PvP refinements
- boss reward pools
- event reward pools
- DD2 completion settlement

The intended result is all-or-nothing ledger and balance updates.

### Boss Contribution Rewards

Future boss rewards should use contribution-based distribution instead of last-hit ownership.

Recommended design:

- track meaningful player damage contribution
- create one configured reward pool per encounter
- divide the pool proportionally
- perform deterministic atomic-unit rounding
- pay only once per encounter
- prevent segmented bosses from generating duplicate reward pools
- record encounter references for idempotency
- verify treasury funding before settlement

Boss progression should use explicit NPC classification rather than relying only on health thresholds.

### Event Reward Pools

Add first-class event reward support for Terraria invasions and world events.

Potential event families include:

- Goblin Army
- Pirate Invasion
- Martian Madness
- Frost Legion
- Pumpkin Moon
- Frost Moon
- Old Ones Army / DD2
- Blood Moon
- Solar Eclipse

Rewards should be tied to meaningful participation and should not be farmable through trivial presence.

### DD2 Completion Settlement

DD2 tracking infrastructure already exists.

Completion payout is intentionally disabled until an atomic multiplayer treasury operation is available.

Normal eligible NPC reward behavior can remain separate from a future configurable DD2 completion bonus.

---

## Blockchain Deposit Roadmap

Blockchain deposits into the gameplay economy are planned but not enabled.

A secure implementation should:

1. observe a real Arkovia transaction
2. verify the destination reserve account
3. verify amount and sender/reference rules
4. wait for configured confirmations
5. reject duplicate or replayed credits
6. credit the off-chain ledger atomically
7. preserve an audit record

Client claims must never be sufficient proof of deposit.

---

## Blockchain Withdrawal Roadmap

Gameplay-to-blockchain withdrawals are planned but not enabled.

The TShock plugin should not directly store the reserve private key.

Recommended architecture:

- reserve or hold the players off-chain amount
- determine the real current Arkovia network fee
- verify amount, fee, and total
- send a narrowly scoped request to a localhost signing service
- submit the blockchain transaction
- store transaction identity and state
- confirm settlement
- finalize the off-chain deduction
- release the hold safely if submission fails before acceptance

---

## Reserve Accounting

Every withdrawable off-chain ARKOS should ultimately be backed by real on-chain reserves.

A future reserve report should show:

- total on-chain reserve
- total off-chain liabilities
- pending withdrawal holds
- available reserve
- coverage ratio

An administrator command such as `/eco reserve` may expose these values once the reserve model is implemented.

---

## Starter Wallet Grant

A future optional starter grant may send a small amount of real ARKOS to newly created player blockchain wallets.

The current target concept is a one-time 10 ARKOS grant for eligible newly created wallets.

This feature is not implemented yet.

Before enabling it, the system should include:

- one-time idempotent grant tracking
- Pending / Submitted / Confirmed / Failed states
- anti-farming controls
- daily grant limits
- minimum reserve thresholds
- transaction fee validation
- secure localhost signing
- full audit history

---

## Player Security PIN

A separate ARKOS security PIN is planned for sensitive economy actions.

Possible protected actions include:

- wallet security changes
- future wallet linking/unlinking
- future withdrawals
- other high-risk account operations

The PIN must not be entered through ordinary logged Terraria command text.

A secure implementation should use private input/session handling or a protected HTTPS workflow and store only a salted password-derived hash.

---

## Wallet Linking and Ownership Verification

Future support for existing external Arkovia wallets should avoid importing private keys into TShock.

Preferred approaches include:

- signed ownership challenges
- secure local enrollment workflows
- protected HTTPS verification

Normal Terraria chat should never be used to submit a real secret phrase or private key.

---

## Economy Presentation and Privacy

Future improvements may include:

- dedicated permission for viewing other players off-chain balances
- configurable privacy rules for player balance lookup
- configurable on-chain public-balance lookup permissions
- more customizable reward/loss message templates
- configurable floating-text behavior

Do not assume these dedicated controls exist until they are implemented.

---

## Shop and Marketplace Extensions

Potential future commerce modules include:

- NPC/server shops with configured buy/sell prices
- stock and progression unlocks
- player marketplace
- item escrow
- listing expiration
- listing fees
- offline settlement
- auction house
- buy orders

Physical Terraria item escrow must be implemented against the exact supported Terraria/TShock version rather than approximated through unsafe inventory mutation.

---

## Jobs, Contracts, and Businesses

Longer-term economy extensions may include:

- jobs and professions
- anti-farm earning caps
- daily and weekly contracts
- quests
- business accounts
- payroll
- region rent
- property taxes

Every currency movement should continue through the authoritative ledger-backed economy API.

---

## Web and Analytics

Potential future administrative tools include:

- TShock-authenticated web dashboard
- economy analytics
- money-supply reporting
- reserve coverage reporting
- transaction and treasury visualizations

Public APIs should continue to expose only intentionally public data.

---

## Security Hardening Roadmap

Ongoing hardening priorities include:

- stronger atomic database transactions for multi-party economy mutations
- replay/idempotency protection for all blockchain-connected mutations
- wallet recovery filesystem hardening
- claim-code strength and single-use enforcement
- trusted-proxy validation for recovery-service deployments
- minimal recovery rendering
- strict local signing-service isolation
- node API exposure review
- safe rate limiting for externally reachable services

---

## Core Invariant

The design principle should remain unchanged:

> Every economy movement must pass through authoritative ledger-backed logic.

Gameplay AI, UI, external plugins, web tools, and blockchain integrations should request transactions through that authoritative layer rather than directly changing balances.
