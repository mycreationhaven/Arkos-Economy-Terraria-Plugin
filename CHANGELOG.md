# 1.4.0-rc.2

- Send TServerWeb's required `TServerWeb Vote Plugin` client identifier for vote and CAPTCHA requests.
- Clarify that TServerWeb requires only its numeric server ID; its provider `ApiKey` remains blank.

# 1.4.0-rc.1

- Add native vote rewards for Terraria-Servers.com and TServerWeb.com.
- Add authenticated `/vote` links, claim, status and TServerWeb CAPTCHA flows.
- Add configurable treasury-backed currency, item and temporary TShock group rewards.
- Add persistent duplicate protection, per-provider limits, combined UTC daily caps and claim throttling.
- Keep provider credentials out of source control and require HTTPS provider communication.

# 1.3.2-rc.1

- Replace long portal login links with account-bound, six-digit one-time codes, expiry and guessing limits.
- Keep strong bearer sessions inside the browser and preserve transaction PIN requirements.
- Add portal sign-in UI and empty transfer-history feedback.
- Retain the progression config loading fix from 1.3.1.

# 1.3.1-rc.1

- Fix JSON deserialization appending saved progression lists to defaults, causing valid 100-rank configurations to abort startup.
- Preserve custom ranks, quests and jobs, including intentionally empty activity lists.
- Add config save/reload/restart regression coverage and verify genuine duplicate ranks still fail without replacing active config.

# 1.3.0-rc.1

- Move plugin logs into its own rotating files.
- Add configurable paid ranks 1–100, activity/XP gates, death demotion, broadcasts, permission perks and one-time items.
- Add owner approval for level-100 administrator access by default.
- Add configurable NPC quests/jobs with daily limits, persistent progress and atomic treasury-funded claims.
- Preserve existing balances and separate PvP split. See docs/PROGRESSION.md for scope and staging requirements.

## 1.2.0-rc.1 — Events and blockchain settlement

- Fixed DD2 victory-hook ordering and limited contribution to event enemies.
- Added durable, atomic multiplayer pools and eight additional event families.
- Added confirmed full-hash deposits, signed-byte withdrawal retries, reserve/fee limits, and guarded expiry refunds.
- Added a separate local signer, private HTTPS PIN portal, persistent PIN lockouts, and one-time permission-controlled starter grants.
- Added deployment/configuration guides and regression coverage. Blockchain features remain disabled until configured; live Terraria/node and MySQL staging remain required.

## 1.1.0 — Currency selection and treasury controls

- Ordinary deaths transfer a configurable wallet percentage (default 25%) to the Terraria Treasury, preserving bank/protected balances and the separate PvP split.
- Added permission-checked `/treasury add` and `/treasury take` with audited, atomic adjustments and overdraft protection.
- Added node-validated `CurrencyId`, currency-aware balance/ledger queries, and independent blockchain-to-economy precision conversion.
- Preserved stored balances with denomination guards, explicit currency-change acceptance, safe reload behavior, and scoped funding baselines.
- Added SQLite and simulated-node regression coverage, including rollback on audit-write failure.

# Changelog

All notable changes to Arkovia Economy are documented here.

---

## Unreleased

### Added

- Configurable off-chain gameplay economy for Terraria.
- NPC kill rewards backed by the internal treasury.
- Configurable reward ranges for common enemies, strong/rare enemies, early bosses, mid-game bosses, end-game bosses, and quests.
- Configurable gameplay reward broadcast modes: `PlayerOnly`, `Nearby`, `Global`, and `Silent`.
- Normal death Wallet penalties with protected Bank balances, cooldown protection, and no negative balances.
- PvP economy penalties with configurable winner/treasury distribution.
- Native Terraria floating/combat-text feedback for positive and negative currency changes.
- Player-created Arkovia blockchain wallets initiated in-game.
- Public Arkovia account identity linked to stable TShock user accounts.
- Protected wallet recovery workflow separated from the normal gameplay economy database.
- Optional local wallet-recovery claim service integration for supported deployments.
- `/arkos balance` for public on-chain balance lookup.
- `/arkos wallet create`, `/arkos wallet address`, `/arkos wallet status`, and `/arkos wallet recovery`.
- Old Ones Army / DD2 event tracking infrastructure.
- Configurable currency presentation for projects building their own economy on Arkovia-compatible infrastructure.
- Public compiled plugin at `release/ArkoviaEconomy.dll`.

### Changed

- Standardized the native currency presentation on `ARKOS`.
- Changed the default `CurrencyName` from `Arkos` to `ARKOS`.
- Expanded documentation for gameplay economy, wallet security, blockchain integration, custom currency deployments, installation, and configuration.
- Clarified the distinction between the off-chain gameplay Wallet, off-chain Bank, and real Arkovia blockchain wallet.
- Clarified that `/bank deposit` and `/bank withdraw` are internal Wallet/Bank movements and are not blockchain deposit/withdraw operations.
- Clarified that the Community & Development funding synchronizer is read-only while wallet generation uses a separate protected recovery workflow.

### Security

- Recovery secrets are kept out of the ordinary TShock economy database and normal configuration.
- Recovery secrets and private keys must never be placed in ordinary Terraria chat or commands.
- Public wallet identity is separated from private recovery material.
- Recovery claim integration uses a separately protected local service in supported deployments.
- Documentation now explicitly separates public blockchain information from sensitive recovery/signing material.

### In Development / Not Yet Enabled

- Blockchain deposits into the gameplay economy.
- Blockchain withdrawals from the gameplay economy.
- Outgoing ARKOS transaction signing.
- Transaction-fee quoting for outgoing payments.
- Automatic starter-wallet ARKOS grants.
- Player security PIN authorization.
- Contribution-based multiplayer boss reward pools.
- Completed DD2 event reward settlement.
- Additional invasion and world-event reward pools.

> DD2 tracking infrastructure exists, but completion payout remains disabled pending a safe atomic multiplayer treasury settlement design.

---

## 1.0.0 - 2026-08-27

- Initial Arkovia Economy TShock project.
- TShock user-backed wallet accounts and banking.
- Immutable transaction ledger.
- Player payments and account history.
- Admin audited adjustments, freeze controls and treasury rewards.
- Read-only Arkovia 5% Community & Development account synchronizer.
- `BLOCK_GENERATED` account-ledger funding recognition with confirmations and duplicate protection.
- In-process plugin API.
- Full documentation and GitHub Actions build workflow.
