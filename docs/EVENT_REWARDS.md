# Multiplayer event rewards

`EventRewards.Enabled` enables configured completion pools independently of ordinary NPC rewards. `GameplayEconomy.Enabled` must also be enabled. Default pools are intentionally small and use the selected economy currency:

| Event | Default pool | Completion rule |
|---|---:|---|
| DD2 / Old Ones Army tiers 1, 2, 3 | 1 / 2 / 3 | `StopInvasion(true)` plus its nested authoritative victory callback, without a reported loss |
| Goblin Army / Frost Legion | 1 each | Observed invasion ends with no remaining invasion size |
| Pirate Invasion / Martian Madness | 2 / 3 | Same invasion-completion rule |
| Blood Moon / Solar Eclipse | 1 / 2 | Natural dawn / dusk transition |
| Pumpkin Moon / Frost Moon | 3 each | Natural dawn; participation completion, not a guaranteed final-wave victory |

Each event needs 30 seconds of observed runtime and each participant needs 100 tracked damage by default. Both are configurable. A pool of zero disables that event's payout. Ordinary per-NPC payouts can coexist with the completion bonus.

Contribution is attributed to the logged-in TShock account. Hits are capped at the target's remaining life and exclude friendly, town, and statue-spawned NPCs. DD2 uses Terraria's explicit Old Ones Army membership table; invasions use Terraria's invasion-group classification; moon/eclipse events use explicit event-enemy sets in `WorldEventTracker`. Merely standing nearby does not qualify.

Qualified participants split a fixed pool proportionally. Largest-remainder allocation conserves every atomic unit with stable user-ID tie-breaking. Disconnected participants still receive their recorded share. Online participants receive a success message after settlement.

The entire pool, every recipient balance, and all ledger entries commit in one database transaction. A unique encounter ID prevents repeat payouts. Insufficient treasury funds, a frozen recipient, or a recipient balance limit leaves the whole event queued for retry; no player receives a partial group payout. `/eco settlement` reports the queue count. Resolve the balance/freeze issue and the worker retries automatically.

Queued completed events survive restarts. Active encounter contributions are held in memory: a shutdown during an event does not award or reconstruct pre-restart participation. Forced moon cancellation outside the natural day/night boundary earns no completion pool. Server administrators can change Terraria world state, so administrator-forced completions cannot be treated as independent evidence of genuine player effort.

The DD2 hook order is verified against the installed Terraria assembly and covered by a nested-callback regression. Multiplayer game-server staging is still required to validate NPC attribution, all event transitions, and player-facing messages under real gameplay.
