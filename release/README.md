# Compiled Plugin

`ArkoviaEconomy.dll` is the compiled **v1.4.0-rc.1** TShock plugin included with this repository. It contains the configurable Terraria-Servers.com and TServerWeb voting-reward integration documented in [`docs/VOTING.md`](../docs/VOTING.md).

Build details:

- Plugin version: `1.4.0-rc.1`
- Target framework: `.NET 9.0`
- TShock package target: `6.1.0`
- SHA-256: `e7b8e89e56975e9863de38f6e510e65154d6f3dc50624d8d944e8c42f0df5968`

## Installation

Copy:

```text
release/ArkoviaEconomy.dll
```

into your TShock server:

```text
ServerPlugins/ArkoviaEconomy.dll
```

Then restart TShock.

The source code used to build the DLL is included in this repository.

## Build from source

```bash
dotnet restore
dotnet build -c Release
```

Normal build output:

```text
bin/Release/net9.0/ArkoviaEconomy.dll
```
