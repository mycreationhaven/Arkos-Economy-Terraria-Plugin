# Validation status — 1.2.0-rc.1

Validated using .NET SDK 9.0.317 and TShock 6.1.0:

```bash
dotnet build -c Release
dotnet run --project tests/ArkoviaEconomy.Tests.csproj -c Release
dotnet build services/ArkoviaSigner/ArkoviaSigner.csproj -c Release
python3 tests/signer_smoke.py
```

The plugin builds successfully with the existing CA1416 wallet-recovery UnixCreateMode warning. The signer builds with zero warnings/errors.

**167 checks pass:** 153 .NET regression checks and 14 separate-signer process checks. These include the previous percentage-death/treasury/custom-currency checks plus real SQLite event settlement and rollback, exact proportional allocation, nested DD2 victory callback ordering, loss/no-double-payout behavior, PIN hashing/change/lockout persistence, real loopback portal origin/session/permission checks, deposit verification/replay/confirmation rejection, withdrawal holds, ambiguous network retry with identical bytes, duplicate outgoing hash rejection, expiry refund guards, grant idempotency, daily grant caps, and native/custom signer restrictions.

Node responses are simulated in tests. The signer process tests use a loopback fake node, fake credentials, and no actual broadcast. Terraria's DD2 StopInvasion IL was inspected to confirm it invokes WinInvasionInternal internally; regression tests simulate that exact callback nesting.

The release includes the compiled plugin DLL and framework-dependent signer ZIP. GitHub Actions repeats the build and tests and publishes both artifacts.

## Required staging checks

Not live-validated in this environment:

- Terraria multiplayer event membership, real damage attribution, completion transitions, command permission dispatch, and green payout messages.
- A real Arkovia reserve/node, actual currency metadata, minimum fees, confirmation/reorganization behavior, and signed-byte broadcast/retry.
- MySQL transaction behavior (SQLite is covered).
- Operator HTTPS reverse-proxy/TLS setup and actual wallet recovery service.

Before enabling reserve spending, follow docs/BLOCKCHAIN_SETUP.md and stage-test a small native/custom deposit, withdrawal, timeout/restart, expired payment, starter grant, and DD2/event completion. Transfers and the portal are disabled by default. Moon-event pools reward participation at natural dawn/dusk; active encounter contributions are not persisted across restarts. Full reserve/liability reporting and forgotten-PIN recovery UI remain outside this release.

## 1.3.0 progression and logging

35 additional checks exercise paid ranks, protected Bank preservation, treasury conservation, XP/activity gates, cooldowns, daily kill caps, job/quest payouts, repeat-claim prevention, failed-claim progress retention, SQL fault rollback after wallet updates, restart persistence, demotion and permission removal, rank-100 approval, one-time item queues and plugin log output. A separate signer smoke run still passes all 14 checks.

The plugin compiles against TShock 6.1.0. Live spawn/kill/death hooks, rank broadcasts, permission interactions, inventory delivery and MySQL progression transactions require staging. Item dispatch is deliberately at-most-once: a crash after durable removal needs manual reconciliation using the logged delivery intent. Default quests/jobs support NPC-kill objectives; other objective types are not implemented.

## 1.3.1-rc.1 startup fix

162 .NET regression checks pass, including nine added config-loading checks. A full default config save/reload reproduced the reported startup exception before the fix. Tests now cover full round-trip loading, fresh-process loading, custom rank costs, replacement job lists, empty quest lists, repeated reload, legacy missing defaults, and rejection of actual duplicate ranks while retaining active config. Signer code is unchanged.

## 1.3.2-rc.1 six-digit access codes

184 .NET checks pass, including real loopback HTTP code exchange with origin and account validation, replay rejection, permission revocation and PIN setup. Dedicated access-code checks cover format, expiry, account binding, five-failure invalidation, global throttling and concurrent one-use redemption. Twelve browser-script checks cover sign-in, hiding/clearing credentials, request headers, quote/confirm buttons and session-expiry recovery; JavaScript syntax also checks successfully. No actual blockchain payments were made. Live browser/Nginx/TShock staging remains required.
