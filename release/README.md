# Compiled Plugin

`ArkoviaEconomy.dll` is the compiled TShock plugin included with this repository.

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
