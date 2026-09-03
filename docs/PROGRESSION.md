# Ranks, quests and jobs (1.3.0 release candidate)

The built-in progression engine is configured under `Progression` in `tshock/ArkoviaEconomy/config.json`. It is independent of the JavaScript/AliasCmd/SEconomy system. No additional script plugin is required. Existing Wallet, Bank, base TShock groups and blockchain balances are retained. Back up the database and config before installing the new DLL.

## Ranking

There are 100 configurable levels, starting at 1. `/rank` shows current progress and the next requirements; `/rank up` (or `/rankup`) buys exactly one level. The configured cost moves from Wallet to Terraria Treasury in the same transaction as the level change and ledger entry. Insufficient funds, frozen accounts, unmet requirements or database failure leave all these values unchanged. Currency amounts use the server's selected economy denomination, including custom currency.

Every accepted death, including PvP, demotes one level; level 1 is the floor. Demotion is separate from the existing ordinary-death percentage and PvP winner/treasury split. XP and accumulated active minutes remain, but the rank-change cooldown restarts and buying the level again costs currency. Rank-ups and demotions are broadcast to all players. Duplicate death packets within two seconds are ignored.

Defaults use cumulative XP and combat-active minutes, plus a 12-hour cooldown after a promotion or demotion:

| Target level | Wallet cost | Total XP | Combat-active minutes |
|---|---:|---:|---:|
| 2 | 0.01 | 25 | 10 |
| 10 | 0.81 | 2,025 | 810 |
| 50 | 24.01 | 60,025 | 24,010 |
| 100 | 98.01 | 245,025 | 98,010 |

For target level L, defaults are `0.01*(L-1)^2` currency, `25*(L-1)^2` XP, and `10*(L-1)^2` minutes. Each rank's values are individually editable; these are starting settings, not a promise about your server's economy. With default quests/jobs, the XP requirement alone takes hundreds of fully capped activity days. Money cannot bypass XP or active time.

An eligible kill gives 1 XP; at most 500 kills count per account per UTC day, with at least 5 seconds between credited kills. A UTC minute containing a credited kill counts as one combat-active minute. This is activity sampling, not exact elapsed playtime, movement tracking or idle login time. NPC objectives use the last eligible attacker, exclude friendly/town/statue NPCs and creatures with under 5 maximum life, and require a logged-in participant at death. Bosses are not specially boosted. These controls discourage simple spam; they do not prove a unique human or eliminate mob farms.

## Permissions, perks and items

Each rank has `Permissions` and `Items`. Permissions from all levels up to the current level are added through TShock's permission hook. Demotion immediately removes permissions unique to the lost level. Existing base-group permissions remain; for example, an existing staff member stays staff after an earned-rank demotion. An explicit denial from an earlier permission hook is respected. Other plugins that check group names directly instead of TShock permissions may need a separate integration.

Example fields on a rank (keep the other rank fields):

```json
{
  "Permissions": ["tshock.tp.home", "yourplugin.perk"],
  "Items": [{ "ItemId": 8, "Stack": 50, "Prefix": 0 }]
}
```

Use actual permission names supported by your installed plugins. Exact names, `prefix.*` and `*` are supported. Operators decide every perk and item; lower ranks default to no added permissions/items. There is no arbitrary console-command execution.

Rank 100 is named **Server Admin** and grants `*`. By default it also needs prior owner approval with `/rankadmin <TShock account ID> approve`; the player must still meet requirements and pay. `/rankadmin <id> revoke` removes approval and rank-100 privileges. This command requires `arkoviaeconomy.admin`, and a real player's **base group** must also have `arkoviaeconomy.rank.approve`; rank-earned permissions cannot satisfy that latter check. Console can approve. Demotion from 100 clears approval. Set `RequireAdminApprovalForLevel100: false` only if you want fully automatic administrative access. Rank 100's wildcard is permission-based; it does not rename the base group to superadmin.

Item rewards are stored when a level is first purchased, and claimed with `/rank claim` while alive. Rebuying a lost rank never awards its items again. Rewards persist across restart. Changing config does not retroactively add rewards for levels already rewarded. The delivery intent is written to the plugin log and removed durably before Terraria dispatch, preventing repeat delivery after restart. A crash between removal and dispatch can lose an item delivery; inspect the logged intent and reconcile manually. Terraria inventory/item delivery is not part of the SQL transaction. Items already received are not confiscated on demotion; permissions are.

## Quests and jobs

This release supports configurable **NPC-kill objectives**. Mining, fishing, crafting, delivery, branching stories and NPC quest-dialogue interfaces are future objective types.

- `/quest` or `/quests`: list quests and stored objective counts.
- `/quest accept <id>`: choose one quest; `/quest leave` stops tracking it.
- `/quest claim`: claim a completed selected quest.
- `/job` or `/jobs`: list jobs.
- `/job join <id>`: choose one job; `/job leave` stops tracking it.
- `/job claim`: collect wages and XP for a completed work batch.

One job and one quest may progress together. Switching preserves each activity's objective count and does not reset its claim quota. Quests/jobs use persistent stable IDs, not display names. Empty `NpcIds` means any eligible hostile NPC; otherwise use the numeric Terraria NPC net IDs. A definition has `Id`, `Name`, `NpcIds`, `RequiredKills`, `Reward`, `Experience`, and `DailyLimit`. Daily quotas reset at UTC midnight; unfinished progress carries over. Claims reset the objective count, allowing repeats up to the quota. Kills beyond a completed objective are not banked for additional claims.

Quest rewards and job wages come from Treasury. A claim commits wallet credit, treasury debit, ledger entry, XP and quota/count changes together. If Treasury is empty, progress stays available to claim later. Separate normal NPC rewards may still be earned. Existing unfinished blockchain operations and balances are untouched.

Grant player command permissions: `arkoviaeconomy.rank`, `arkoviaeconomy.quests`, `arkoviaeconomy.jobs`. Enabling progression does not automatically modify existing groups. Configuration reload validates the definitions before replacing active config. Use `Progression.Enabled: false` to disable commands, tracking and earned permissions while retaining saved progress. Avoid reusing retired activity IDs for unrelated objectives; doing so reuses their saved counts.

## Staging checklist

Test with two real logged-in accounts: enemy attribution, a PvP and non-PvP death, both broadcasts, permissions before/after demotion, one-time item delivery, quest/job claims with empty and funded Treasury, restart persistence, and rank-100 approval/revocation. Automated SQLite tests cover accounting and state transitions; live Terraria hooks, inventory delivery, compatibility with other permission plugins, and MySQL require staging.

Suggested next additions: fishing/mining objectives with placed-block protections, shared boss quests, seasonal cosmetic leaderboards, prestige after the level cap, and configurable moderation approval for powerful perks. Keep administrative access subject to trust as well as progression.
