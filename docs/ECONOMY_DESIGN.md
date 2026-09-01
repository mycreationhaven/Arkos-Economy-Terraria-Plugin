# Economy design

## Monetary policy

The recommended operating model starts player accounts at zero and introduces new game currency only when the Arkovia 5% Community & Development account receives confirmed fee distributions. This makes blockchain activity the external monetary inflow.

Existing currency can circulate repeatedly:

```text
blockchain funding -> treasury -> rewards -> players -> fees/services -> treasury
```

The plugin distinguishes **new funding** from **recycled money**. Funding records correspond to confirmed on-chain fee credits. Player payments and recycled fees do not increase total game monetary backing.

## Solvency

Treasury reward calls debit the internal treasury first. If the treasury is insufficient, rewards fail instead of silently minting currency. This is the foundation for jobs, bosses, quests, events and other reward plugins.

## Identity

Balances belong to authenticated TShock accounts. This prevents a player from changing a Terraria character name to impersonate another economic identity.

## Auditability

Manual grants are allowed only through explicit privileged commands and always create ledger records including the actor and required reason. Normal reward systems should pay from treasury rather than use admin grants.

## Sinks

Useful sinks include server shops, event entry fees, teleport/service fees, marketplace fees, cosmetic services, region rent, repair services and optional taxes. When configured to recycle, these flows replenish the treasury rather than destroy money.

## Inflation controls

Recommended controls:

1. Starting balance = 0.
2. Treasury-backed rewards only.
3. Reward budgets tied to treasury balance.
4. Avoid paying for infinitely farmable actions without caps/cooldowns.
5. Use market/service sinks.
6. Track total player money, treasury money, new blockchain funding and transaction volume separately.
