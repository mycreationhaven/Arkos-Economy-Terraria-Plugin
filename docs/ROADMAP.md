# Extension modules roadmap

The v1 codebase deliberately makes the **financial core and Arkovia treasury bridge** authoritative first. The shop and market database schemas and permissions are already reserved, but physical Terraria item escrow should not be faked: secure inventory mutation must be validated against the exact TShock/Terraria version before production marketplace release.

Recommended modules on top of the v1 API:

- NPC/server shop engine with configured buy/sell prices, stock and progression unlocks
- Player marketplace with actual item escrow, expirations, listing fees and offline settlement
- Jobs/professions with anti-farm caps and treasury budgets
- Boss/event bounty plugin that rewards contributors from treasury
- Daily/weekly contracts and quests
- Business accounts and payroll
- Region rent/property taxes
- Auction house and buy orders
- Web dashboard through TShock-authenticated REST adapter
- Economy analytics and money-supply reports
- Optional isolated Arkovia deposit/withdraw bridge

The invariant should remain unchanged: every currency movement goes through the ledger-backed API.
