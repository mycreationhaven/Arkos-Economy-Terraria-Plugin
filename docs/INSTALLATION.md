# Installation and operations

1. Install a current TShock 6.1.x server.
2. Install the .NET 9 SDK on the build machine.
3. Build Arkovia Economy with `dotnet build -c Release`.
4. Copy `ArkoviaEconomy.dll` into TShock's `ServerPlugins` folder.
5. Start TShock once so `tshock/ArkoviaEconomy/config.json` is generated.
6. Stop the server and review the config.
7. Run an Arkovia node locally or configure a trusted HTTPS node URL.
8. Confirm the Arkovia node account ledger tracks `ARK-KVFL-C6EE-2UD2-CSJ8Q`.
9. Grant player and admin permissions using TShock groups.
10. Restart and use `/treasury` to verify sync status.

## First production test

Set `GameAllocationPercent` conservatively if desired, then create real Arkovia fee activity. Wait for the configured confirmation count and verify the funding record appears once and the Terraria Treasury increases by the expected allocation.

Then test `/eco reward <test-account> <amount> <reason>`. The player's balance should increase while treasury decreases by the identical amount.

## Upgrades

Always back up the TShock database and the `tshock/ArkoviaEconomy` configuration folder before replacing the DLL. Database tables are created/checked on startup.
