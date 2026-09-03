# Validation status — 1.1.0

Validated with .NET SDK 9.0.317 against TShock 6.1.0:

```bash
dotnet build -c Release
dotnet run --project tests/ArkoviaEconomy.Tests.csproj -c Release
```

Build succeeds. The existing CA1416 warning in wallet-recovery code remains (`UnixCreateMode` on Windows).

The executable regression suite passes 61 checks using real SQLite database operations and simulated Nxt/Arkovia node responses. It covers percentage death loss, rounding, protected funds, bank preservation, PvP split conservation, treasury adjustments/overdrafts/audit actors, rollback on audit insertion failure, denomination guards, selected-currency metadata and balance queries, invalid node data, funding conversion, and repeated-sync idempotency.

The release DLL is built from this source. GitHub Actions repeats the build and regression suite and uploads the DLL as an artifact.

Not live-tested here: Terraria death packet handling/cooldowns, in-game command permission dispatch, a running Arkovia node with an actual custom currency, or MySQL. Before server rollout, stage those checks and confirm the intended currency ID, source account, protected minimum, and PvP settings. Follow the upgrade procedure in `docs/CONFIGURATION.md` before changing an existing economy's currency.
