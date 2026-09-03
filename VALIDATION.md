# Validation status — 1.2.0-rc.1

Validated using .NET SDK 9.0.317 and TShock 6.1.0:

```bash
dotnet build -c Release
dotnet run --project tests/ArkoviaEconomy.Tests.csproj -c Release
dotnet build services/ArkoviaSigner/ArkoviaSigner.csproj -c Release
python3 tests/signer_smoke.py
```

The plugin builds successfully with the existing CA1416 wallet-recovery UnixCreateMode warning. The signer builds with zero warnings/errors.

**132 checks pass:** 118 .NET regression checks and 14 separate-signer process checks. These include the previous percentage-death/treasury/custom-currency checks plus real SQLite event settlement and rollback, exact proportional allocation, nested DD2 victory callback ordering, loss/no-double-payout behavior, PIN hashing/change/lockout persistence, real loopback portal origin/session/permission checks, deposit verification/replay/confirmation rejection, withdrawal holds, ambiguous network retry with identical bytes, duplicate outgoing hash rejection, expiry refund guards, grant idempotency, daily grant caps, and native/custom signer restrictions.

Node responses are simulated in tests. The signer process tests use a loopback fake node, fake credentials, and no actual broadcast. Terraria's DD2 StopInvasion IL was inspected to confirm it invokes WinInvasionInternal internally; regression tests simulate that exact callback nesting.

The release includes the compiled plugin DLL and framework-dependent signer ZIP. GitHub Actions repeats the build and tests and publishes both artifacts.

## Required staging checks

Not live-validated in this environment:

- Terraria multiplayer event membership, real damage attribution, completion transitions, command permission dispatch, and green payout messages.
- A real Arkovia reserve/node, actual currency metadata, minimum fees, confirmation/reorganization behavior, and signed-byte broadcast/retry.
- MySQL transaction behavior (SQLite is covered).
- Operator HTTPS reverse-proxy/TLS setup and actual wallet recovery service.

Before enabling reserve spending, follow docs/BLOCKCHAIN_SETUP.md and stage-test a small native/custom deposit, withdrawal, timeout/restart, expired payment, starter grant, and DD2/event completion. Transfers and the portal are disabled by default. Moon-event pools reward participation at natural dawn/dusk; active encounter contributions are not persisted across restarts. Full reserve/liability reporting and forgotten-PIN recovery UI remain outside this release.
