# Release Artifacts

This directory documents the current Arkovia Terraria/TShock release artifacts.

## Current release line

- Release: `v1.5.0-rc.1`
- Main plugin: `ArkoviaEconomy.dll`
- Companion crossplay plugin: `ArkoviaCrossplay.dll`
- Target runtime: `.NET 9`
- Target server stack: TShock 6.1 / Terraria 1.4.5.x

The published GitHub `v1.5.0-rc.1` release currently contains the main `ArkoviaEconomy.dll` and checksum. The crossplay companion was added after that release and is built from:

```text
crossplay/ArkoviaCrossplay/
```

Until a newer GitHub release includes both DLLs, build `ArkoviaCrossplay.dll` from source or use the validated server build for deployment.

## ArkoviaEconomy.dll

Version 1.5 includes the current marketplace, towns/property, live inventory, item escrow, stocks/holdings, voting, progression, atomic settlement and secure web integration work.

Build from source:

```bash
dotnet restore
dotnet build -c Release
```

Normal output:

```text
bin/Release/net9.0/ArkoviaEconomy.dll
```

Install to:

```text
ServerPlugins/ArkoviaEconomy.dll
```

Then perform a full TShock restart.

## ArkoviaCrossplay.dll

The companion crossplay bridge provides approved Terraria 1.4.5.x PC/mobile handshake compatibility.

Build:

```bash
dotnet build crossplay/ArkoviaCrossplay/ArkoviaCrossplay.csproj -c Release
```

Output:

```text
crossplay/ArkoviaCrossplay/bin/Release/net9.0/ArkoviaCrossplay.dll
```

Install to:

```text
ServerPlugins/ArkoviaCrossplay.dll
```

The validated production build installed on the Arkovia host has SHA-256:

```text
0a1acad963a8b0c7ba0c8d817ba359d39a938d8cc235e2027821a13559a8d3dc
```

It was built with 0 warnings and 0 errors and successfully initialized on TShock 6.1 / Terraria 1.4.5.8.

### Crossplay status

- PC: supported normally.
- Mobile 1.4.5.x: bridge deployed and active; real-device end-to-end validation is still ongoing.
- Xbox / PlayStation / Nintendo Switch: not enabled by this plugin alone. Console platform networking/discovery support is still required.

The bridge is intentionally allow-list based. Unknown Terraria protocols are not automatically accepted.

Administration:

```text
/arcrossplay info
/arcrossplay versions
/arcrossplay verbose
/arcrossplay reload
```

Permission:

```text
arkovia.crossplay.admin
```

## Release safety

Before replacing either DLL on a live server:

1. Broadcast a restart notice.
2. Save the Terraria world.
3. Shut TShock down cleanly.
4. Replace the DLL(s).
5. Start TShock.
6. Verify port 7777 is listening.
7. Confirm both plugins initialized successfully in the TShock log.
8. Test login and critical economy/marketplace workflows.

Do not commit secrets, REST tokens, signing keys, recovery phrases or production environment files to the repository.
