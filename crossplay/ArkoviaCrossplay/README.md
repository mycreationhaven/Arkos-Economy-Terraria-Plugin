# Arkovia Crossplay Bridge

`ArkoviaCrossplay.dll` is an optional TShock 6.1 / .NET 9 compatibility bridge for Terraria **1.4.5.x PC and mobile clients**.

It intercepts the initial Terraria `ConnectRequest` version string and, only for explicitly approved 1.4.5.x protocol versions, rewrites that handshake to the live server protocol (`Terraria` + `Main.curRelease`). The actual server version is detected at runtime, so the bridge does not hard-code Arkovia's current server protocol.

## Important scope

This first version is deliberately conservative:

- Supports explicitly approved **Terraria 1.4.5.x PC/mobile** handshake versions.
- Does **not** blindly accept arbitrary older Terraria versions.
- Does **not** translate arbitrary gameplay packets.
- Does **not** provide Xbox/PlayStation/Switch networking or bypass platform networking restrictions.
- Does **not** claim full console crossplay before Terraria's official console/PC crossplay layer is available and verified.

The bridge works best where point releases remain packet-compatible and the only blocker is Terraria's version-string mismatch check. If a point release changes packet layout, that protocol should not be approved until tested.

## Default approved protocols

| Protocol | Known version |
| --- | --- |
| `Terraria311` | 1.4.5.0 |
| `Terraria312` | 1.4.5.1 |
| `Terraria313` | 1.4.5.x compatibility protocol |
| `Terraria314` | 1.4.5.x compatibility protocol |
| `Terraria315` | 1.4.5.2 |
| `Terraria316` | 1.4.5.3 |
| `Terraria317` | 1.4.5.4 |
| `Terraria318` | 1.4.5.5 |
| `Terraria319` | 1.4.5.6 |

The server's current native protocol is accepted automatically and never needs to be listed.

## Installation

Build the project and place `ArkoviaCrossplay.dll` in the same TShock `ServerPlugins` directory as `ArkoviaEconomy.dll`, then restart TShock.

A configuration file is created at:

```text
tshock/ArkoviaCrossplay.json
```

Example:

```json
{
  "Enabled": true,
  "Verbose": false,
  "AllowedClientProtocols": {
    "Terraria318": "Terraria 1.4.5.5 (PC/mobile)",
    "Terraria319": "Terraria 1.4.5.6 (PC/mobile)"
  }
}
```

Do not add unknown protocols merely to bypass a kick. Test each new version against movement, inventory, chests, NPCs, projectiles, world sync, SSC/login, PvP, and disconnect/reconnect before approving it for production.

## Commands

Requires permission `arkovia.crossplay.admin`:

```text
/arcrossplay info
/arcrossplay versions
/arcrossplay verbose
/arcrossplay reload
```

Alias: `/acp`.

Verbose mode records the version string sent by connecting clients and is useful for identifying a newly released mobile build.

## Console plan

As of September 2026, Terraria's developers are still publicly describing full crossplay as work that follows the synchronized 1.4.5 updates. Console support therefore has two separate requirements:

1. **Game protocol compatibility** — something this bridge can potentially help with after testing.
2. **Console network/session connectivity** — Xbox, PlayStation, and Switch do not necessarily expose the same arbitrary-IP server workflow as PC/mobile. That cannot be solved safely by rewriting a Terraria handshake string.

Arkovia should treat console support as a separate integration phase once Terraria's official crossplay/network layer is released and documented. The current bridge is structured so the server-side protocol compatibility layer can remain useful when that happens.

## Credits and references

The design is informed by the open-source `Moneylover3246/Crossplay` project and the newer `magaflaca/MobileCrossplay` approach for TShock 6 / Terraria 1.4.5.x. Arkovia's bridge keeps the allow-list fail-closed and dynamically detects the live server protocol rather than hard-coding a target release.
