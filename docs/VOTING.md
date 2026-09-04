# Vote Rewards

Arkovia Economy 1.4 adds native vote rewards for Terraria-Servers.com and TServerWeb.com. It does
not require either provider's legacy TShock DLL.

## Commands

| Command | Purpose |
|---|---|
| `/vote` or `/vote links` | Show enabled voting links |
| `/vote claim` | Verify and claim an unclaimed Terraria-Servers.com vote |
| `/vote claim terraria-servers` | Claim one configured provider explicitly |
| `/vote tserverweb` | Begin TServerWeb's in-game vote/CAPTCHA flow |
| `/vote tserverweb <answer>` | Answer the pending TServerWeb CAPTCHA |
| `/vote status` | Show today's provider and combined claim counts |

Permission: `arkoviaeconomy.vote`. Players must also be logged into a TShock account. The stable
TShock account ID, rather than an IP address or character name, owns each reward claim.

## Configuration

Copy the `Voting` section from `examples/config.example.json` into the active configuration. Keep
`Enabled` false until the provider IDs, API key and voting URLs are correct. Never commit a live API
key. Restart after first enabling the module; ordinary reward changes can use `/eco reload`.

`CurrencyAmount` uses the server's configured off-chain currency. It displays ARKOS for a native
deployment or the validated custom Arkovia currency symbol. Vote rewards debit the Terraria Treasury;
they do not create an on-chain transaction for every vote.

Item rewards use Terraria item ID, stack and prefix values. Test every item on a staging server.

Permission rewards use temporary TShock groups. Create a narrowly scoped group first, then place its
name and duration in `Groups`. The voting module never grants raw permissions or overwrites a player's
permanent account group. Do not use owner, superadmin, REST, wallet-security, treasury-administration
or wildcard groups as vote rewards.

## Caps and duplicate protection

Each supported provider currently permits one rewarded claim per TShock account per UTC day. The
combined `MaximumRewardedVotesPerAccountPerDay` determines whether a player may receive rewards from
both providers. A unique persistent claim key prevents restarts or repeated commands from paying the
same provider/day twice. `ClaimCooldownSeconds` throttles remote checks.

## Provider behavior

Terraria-Servers.com uses its vote claim API over HTTPS. The player must vote with the exact TShock
account name used in-game. TServerWeb uses its HTTPS in-game voting and CAPTCHA response flow.

Provider responses are size-limited and time-limited. A provider outage fails closed: no unverified
currency, items or groups are awarded.

## Production checklist

1. Back up the TShock and Arkovia Economy databases.
2. Create any reward groups with only the intended permissions.
3. Fund the internal Terraria Treasury.
4. Configure one provider and small test rewards.
5. Restart TShock and confirm Arkovia Economy 1.4 loads.
6. Test one authenticated account through voting, CAPTCHA, reward, repeated claim and next-day claim.
7. Confirm the currency ledger and `ArkoviaVoteClaims` record agree.
8. Test item persistence with server-side characters if SSC is enabled.
9. Enable the second provider only after the first passes staging.

This release candidate requires live provider and TShock staging before production use.
