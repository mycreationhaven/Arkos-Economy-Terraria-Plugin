# Compiled Plugin

`ArkoviaEconomy.dll` is the compiled **v1.4.0-rc.2** TShock plugin included with this repository. It contains the configurable Terraria-Servers.com and TServerWeb voting-reward integration and the required TServerWeb client-identification correction documented in [`docs/VOTING.md`](../docs/VOTING.md).

Build details:

- Plugin version: `1.4.0-rc.2`
- Target framework: `.NET 9.0`
- TShock package target: `6.1.0`
- SHA-256: `b0558229197864b6852f4855ac7eda119462250c096725e2ad5050f59285f539`

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
