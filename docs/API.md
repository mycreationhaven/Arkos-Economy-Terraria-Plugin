# Developer API

Arkovia Economy exposes an in-process C# API so other TShock plugins can participate without touching economy tables directly.

```csharp
using ArkoviaEconomy.Api;

var api = ArkoviaEconomyApi.Instance;
if (api is null)
    return;

var treasury = api.GetTreasury();
var player = api.GetOrCreatePlayer(tshockUserId, accountName);
```

## Transfer

```csharp
api.Transfer(
    fromUserId,
    fromName,
    toUserId,
    toName,
    amountAtomic: 250_000_000, // 2.5 ARK at 8 decimals
    referenceType: "quest",
    referenceId: "quest-iron-001",
    description: "Quest settlement",
    actor: "MyQuestPlugin");
```

## Treasury-backed reward

```csharp
api.RewardFromTreasury(
    userId,
    userName,
    amountAtomic: 100_000_000,
    referenceType: "boss_reward",
    referenceId: "eye-of-cthulhu:world-01:2026-08-27",
    description: "Eye of Cthulhu bounty",
    actor: "BossBountyPlugin");
```

If the treasury does not have enough funds, the call throws instead of creating currency.

## Event

```csharp
api.TransactionCompleted += (_, e) =>
{
    var tx = e.Transaction;
    // analytics, achievements, audit integrations, etc.
};
```

## Contract rule

External plugins should never execute SQL against `ArkoviaEconomyAccounts` to change balances. Use the API so the immutable ledger, balance checks, freeze checks, limits, and treasury rules remain intact.

## HTTP/REST

v1 intentionally does **not** open a second unauthenticated HTTP server. TShock already has a token-based REST system. For external web dashboards, the recommended extension is a small TShock REST adapter that calls this in-process API and uses TShock REST authentication and permissions. This keeps one authentication surface instead of silently exposing financial endpoints.
