using System.Collections.Concurrent;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;

namespace ArkoviaEconomy.Gameplay;

public sealed class GameplayEconomyHandler : IDisposable
{
    private readonly TerrariaPlugin _plugin;
    private readonly EconomyService _economy;
    private readonly Func<EconomyConfig> _config;

    private readonly ConcurrentDictionary<int, DateTime>
        _lastDeathPenaltyUtc = new();

    private readonly ConcurrentDictionary<int, DateTime>
        _lastPvpPenaltyUtc = new();

    // NPC whoAmI -> last valid TShock player index
    private readonly ConcurrentDictionary<int, int>
        _lastNpcAttacker = new();

    // Old One's Army / DD2 encounter tracking.
    //
    // This first implementation intentionally tracks contribution and
    // authoritative win/loss state without paying currency. Event payouts
    // will be enabled only after an atomic multiplayer treasury operation
    // is added to EconomyService.
    private readonly object _dd2Sync = new();

    // TShock user ID -> damage dealt during the active DD2 encounter.
    private readonly Dictionary<int, long>
        _dd2DamageByUserId = new();

    private bool _dd2SessionActive;
    private bool _dd2VictoryPending;
    private bool _dd2LossReported;
    private int _dd2Difficulty;
    private DateTime _dd2StartedUtc;

    private static readonly FieldInfo? SourcePlayerIndexField =
        typeof(PlayerDeathReason).GetField(
            "_sourcePlayerIndex",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);

    private bool _registered;

    public GameplayEconomyHandler(
        TerrariaPlugin plugin,
        EconomyService economy,
        Func<EconomyConfig> config)
    {
        _plugin = plugin;
        _economy = economy;
        _config = config;
    }

    public void Register()
    {
        if (_registered)
            return;

        GetDataHandlers.KillMe.Register(OnKillMe);

        ServerApi.Hooks.NpcStrike.Register(
            _plugin,
            OnNpcStrike);

        ServerApi.Hooks.NpcKilled.Register(
            _plugin,
            OnNpcKilled);

        On.Terraria.GameContent.Events.DD2Event.StartInvasion +=
            OnDd2StartInvasion;

        On.Terraria.GameContent.Events.DD2Event.ReportLoss +=
            OnDd2ReportLoss;

        On.Terraria.GameContent.Events.DD2Event.WinInvasionInternal +=
            OnDd2WinInvasionInternal;

        On.Terraria.GameContent.Events.DD2Event.StopInvasion +=
            OnDd2StopInvasion;

        _registered = true;

        if (SourcePlayerIndexField == null)
        {
            TShock.Log.ConsoleWarn(
                "[ArkoviaEconomy] PvP killer attribution is unavailable. " +
                "PvP currency transfers will fail closed.");
        }

        TShock.Log.ConsoleInfo(
            "[ArkoviaEconomy] Gameplay death, PvP, NPC and boss " +
            "economy hooks registered.");
    }

    // ---------------------------------------------------------------------
    // NPC REWARDS
    // ---------------------------------------------------------------------

    private void OnNpcStrike(
        NpcStrikeEventArgs args)
    {
        try
        {
            var cfg = _config();

            if (!cfg.GameplayEconomy.Enabled)
                return;

            if (args.Npc == null ||
                args.Player == null)
            {
                return;
            }

            if (args.Npc.whoAmI < 0 ||
                args.Npc.whoAmI >= Main.maxNPCs)
            {
                return;
            }

            var playerIndex = args.Player.whoAmI;

            if (playerIndex < 0 ||
                playerIndex >= TShock.Players.Length)
            {
                return;
            }

            var tsPlayer = TShock.Players[playerIndex];

            if (tsPlayer == null ||
                !tsPlayer.Active ||
                !tsPlayer.RealPlayer ||
                !tsPlayer.IsLoggedIn ||
                tsPlayer.Account == null)
            {
                return;
            }

            // Do not track hits against friendly/town NPCs.
            if (args.Npc.friendly ||
                args.Npc.townNPC)
            {
                return;
            }

            _lastNpcAttacker[args.Npc.whoAmI] =
                playerIndex;

            TrackDd2Contribution(
                tsPlayer,
                args.Npc,
                args.Damage);
        }
        catch (Exception ex)
        {
            TShock.Log.Error(
                $"[ArkoviaEconomy] NPC strike tracking failed: {ex}");
        }
    }

    private void TrackDd2Contribution(
        TSPlayer player,
        NPC npc,
        int damage)
    {
        if (damage <= 0 ||
            player.Account == null)
        {
            return;
        }

        lock (_dd2Sync)
        {
            if (!_dd2SessionActive)
                return;

            // Fail closed if Terraria no longer considers DD2 active.
            if (!Terraria.GameContent.Events.DD2Event.Ongoing)
                return;

            long existing =
                _dd2DamageByUserId.TryGetValue(
                    player.Account.ID,
                    out var current)
                    ? current
                    : 0L;

            _dd2DamageByUserId[player.Account.ID] =
                existing > long.MaxValue - damage
                    ? long.MaxValue
                    : existing + damage;
        }
    }

    // ---------------------------------------------------------------------
    // OLD ONE'S ARMY / DD2 EVENT TRACKING
    // ---------------------------------------------------------------------

    private void OnDd2StartInvasion(
        On.Terraria.GameContent.Events.DD2Event.orig_StartInvasion orig,
        int difficultyOverride)
    {
        // Let Terraria initialize the invasion first.
        orig(difficultyOverride);

        lock (_dd2Sync)
        {
            _dd2DamageByUserId.Clear();

            _dd2SessionActive =
                Terraria.GameContent.Events.DD2Event.Ongoing;

            _dd2VictoryPending = false;
            _dd2LossReported = false;

            _dd2Difficulty =
                Terraria.GameContent.Events.DD2Event.OngoingDifficulty;

            if (_dd2Difficulty <= 0)
                _dd2Difficulty = difficultyOverride;

            _dd2StartedUtc = DateTime.UtcNow;
        }

        TShock.Log.ConsoleInfo(
            $"[ArkoviaEconomy] DD2 session started. " +
            $"Difficulty={_dd2Difficulty}, " +
            $"Ongoing={Terraria.GameContent.Events.DD2Event.Ongoing}.");
    }

    private void OnDd2ReportLoss(
        On.Terraria.GameContent.Events.DD2Event.orig_ReportLoss orig)
    {
        orig();

        lock (_dd2Sync)
        {
            if (_dd2SessionActive)
                _dd2LossReported = true;

            _dd2VictoryPending = false;
        }

        TShock.Log.ConsoleInfo(
            "[ArkoviaEconomy] DD2 loss reported.");
    }

    private void OnDd2WinInvasionInternal(
        On.Terraria.GameContent.Events.DD2Event.orig_WinInvasionInternal orig)
    {
        // Mark the authoritative Terraria victory path before continuing
        // through the game's normal victory processing.
        lock (_dd2Sync)
        {
            if (_dd2SessionActive)
                _dd2VictoryPending = true;
        }

        orig();

        TShock.Log.ConsoleInfo(
            "[ArkoviaEconomy] DD2 WinInvasionInternal observed.");
    }

    private void OnDd2StopInvasion(
        On.Terraria.GameContent.Events.DD2Event.orig_StopInvasion orig,
        bool win)
    {
        Dictionary<int, long> contributions;
        bool sessionActive;
        bool victoryPending;
        bool lossReported;
        int difficulty;
        DateTime startedUtc;

        lock (_dd2Sync)
        {
            sessionActive = _dd2SessionActive;
            victoryPending = _dd2VictoryPending;
            lossReported = _dd2LossReported;
            difficulty = _dd2Difficulty;
            startedUtc = _dd2StartedUtc;

            contributions =
                new Dictionary<int, long>(
                    _dd2DamageByUserId);

            _dd2SessionActive = false;
            _dd2VictoryPending = false;
            _dd2LossReported = false;
            _dd2Difficulty = 0;
            _dd2DamageByUserId.Clear();
        }

        // Preserve Terraria's normal behavior.
        orig(win);

        if (!sessionActive)
            return;

        var totalDamage =
            contributions.Values.Aggregate(
                0L,
                (total, value) =>
                    total > long.MaxValue - value
                        ? long.MaxValue
                        : total + value);

        var duration =
            DateTime.UtcNow - startedUtc;

        var confirmedVictory =
            win &&
            victoryPending &&
            !lossReported;

        TShock.Log.ConsoleInfo(
            $"[ArkoviaEconomy] DD2 session ended. " +
            $"WinArgument={win}, " +
            $"VictoryPending={victoryPending}, " +
            $"LossReported={lossReported}, " +
            $"ConfirmedVictory={confirmedVictory}, " +
            $"Difficulty={difficulty}, " +
            $"Participants={contributions.Count}, " +
            $"TrackedDamage={totalDamage}, " +
            $"DurationSeconds={(long)duration.TotalSeconds}.");

        if (confirmedVictory)
        {
            // Intentionally no ARKOS payout yet.
            //
            // The next stage adds an atomic treasury-backed multiplayer
            // payout so an event can never partially reward a group.
            TShock.Log.ConsoleInfo(
                "[ArkoviaEconomy] DD2 victory confirmed. " +
                "Reward settlement is currently disabled pending " +
                "atomic multiplayer payout support.");
        }
    }

    private void OnNpcKilled(
        NpcKilledEventArgs args)
    {
        try
        {
            var npc = args.npc;

            if (npc == null)
                return;

            var cfg = _config();

            var npcName =
                Lang.GetNPCNameValue(npc.netID);

            if (string.IsNullOrWhiteSpace(npcName))
                npcName = $"NPC {npc.netID}";

            void LogDecision(
                string result,
                string reason,
                int? playerIndex = null,
                long rewardAtomic = 0)
            {
                if (!cfg.GameplayEconomy.LogNpcRewardDecisions)
                    return;

                TShock.Log.ConsoleInfo(
                    $"[ArkoviaEconomy] NPC reward decision: " +
                    $"NPC={npcName}, NetID={npc.netID}, " +
                    $"WhoAmI={npc.whoAmI}, Boss={npc.boss}, " +
                    $"LifeMax={npc.lifeMax}, Statue={npc.SpawnedFromStatue}, " +
                    $"Player={(playerIndex.HasValue ? playerIndex.Value.ToString() : "none")}, " +
                    $"Result={result}, Reason={reason}, RewardAtomic={rewardAtomic}.");
            }

            // Always remove attribution when this NPC slot dies.
            if (!_lastNpcAttacker.TryRemove(
                    npc.whoAmI,
                    out var playerIndex))
            {
                LogDecision("SKIPPED", "NO_KILLER_ATTRIBUTION");
                return;
            }

            if (!cfg.GameplayEconomy.Enabled)
            {
                LogDecision("SKIPPED", "GAMEPLAY_ECONOMY_DISABLED", playerIndex);
                return;
            }

            if (npc.friendly ||
                npc.townNPC ||
                npc.lifeMax <= 0)
            {
                LogDecision("SKIPPED", "INELIGIBLE_NPC", playerIndex);
                return;
            }

            // Prevent obvious statue farming.
            if (npc.SpawnedFromStatue)
            {
                LogDecision("SKIPPED", "STATUE_FARM_PROTECTION", playerIndex);
                return;
            }

            if (playerIndex < 0 ||
                playerIndex >= TShock.Players.Length)
            {
                LogDecision("SKIPPED", "INVALID_PLAYER_INDEX", playerIndex);
                return;
            }

            var player =
                TShock.Players[playerIndex];

            if (player == null ||
                !player.Active ||
                !player.RealPlayer ||
                !player.IsLoggedIn ||
                player.Account == null)
            {
                LogDecision("SKIPPED", "PLAYER_NOT_ELIGIBLE", playerIndex);
                return;
            }

            var rewardRange =
                SelectRewardRange(
                    npc,
                    cfg.GameplayEconomy.Rewards,
                    out var rewardClass);

            if (rewardRange == null ||
                !rewardRange.Enabled)
            {
                LogDecision("SKIPPED", "REWARD_CLASS_DISABLED", playerIndex);
                return;
            }

            var minimumAtomic =
                cfg.ToAtomic(rewardRange.Minimum);

            var maximumAtomic =
                cfg.ToAtomic(rewardRange.Maximum);

            if (minimumAtomic <= 0 ||
                maximumAtomic <= 0 ||
                maximumAtomic < minimumAtomic)
            {
                LogDecision("SKIPPED", "INVALID_REWARD_RANGE", playerIndex);
                return;
            }

            var rewardAtomic =
                minimumAtomic == maximumAtomic
                    ? minimumAtomic
                    : Random.Shared.NextInt64(
                        minimumAtomic,
                        checked(maximumAtomic + 1));

            if (rewardAtomic <= 0)
            {
                LogDecision("SKIPPED", "ZERO_REWARD", playerIndex);
                return;
            }

            var account =
                _economy.GetOrCreatePlayer(
                    player.Account.ID,
                    player.Name);

            var referenceId =
                $"npc:{npc.netID}:{npc.whoAmI}:" +
                $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:" +
                $"{Guid.NewGuid():N}";

            long actualReward;

            try
            {
                actualReward =
                    _economy.ApplyGameplayReward(
                        account,
                        rewardAtomic,
                        "npc_kill",
                        referenceId,
                        $"{rewardClass} reward for defeating {npcName}",
                        player.Name);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "Treasury does not have enough funds",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Treasury-backed economy:
                // no money is created if the treasury cannot fund it.
                LogDecision(
                    "SKIPPED",
                    "TREASURY_INSUFFICIENT",
                    playerIndex,
                    rewardAtomic);
                return;
            }

            if (actualReward <= 0)
            {
                LogDecision(
                    "SKIPPED",
                    "ECONOMY_RETURNED_ZERO",
                    playerIndex,
                    rewardAtomic);
                return;
            }

            ShowFloatingCurrencyChange(
                player,
                cfg,
                actualReward,
                true);

            LogDecision(
                "AWARDED",
                rewardClass,
                playerIndex,
                actualReward);
        }
        catch (Exception ex)
        {
            TShock.Log.Error(
                $"[ArkoviaEconomy] NPC reward handler failed: {ex}");
        }
    }

    private static GameplayRewardRange? SelectRewardRange(
        NPC npc,
        GameplayRewardRangesConfig rewards,
        out string rewardClass)
    {
        if (npc.boss)
        {
            // Initial classification by boss max health.
            //
            // We can replace this later with explicit boss IDs /
            // progression tiers and multiplayer contribution splitting.

            if (npc.lifeMax <= 15000)
            {
                rewardClass = "early_boss";
                return rewards.EarlyBoss;
            }

            if (npc.lifeMax <= 50000)
            {
                rewardClass = "mid_boss";
                return rewards.MidBoss;
            }

            rewardClass = "end_game_boss";
            return rewards.EndGameBoss;
        }

        // Rare NPCs and higher-health enemies receive the
        // Strong/Rare range.
        if (npc.rarity > 0 ||
            npc.lifeMax >= 1000)
        {
            rewardClass = "strong_rare_enemy";
            return rewards.StrongRareEnemy;
        }

        rewardClass = "common_enemy";
        return rewards.CommonEnemy;
    }

    private static void ShowFloatingCurrencyChange(
        TSPlayer player,
        EconomyConfig cfg,
        long amountAtomic,
        bool isGain)
    {
        if (amountAtomic <= 0 ||
            player == null ||
            !player.Active ||
            !player.RealPlayer)
        {
            return;
        }

        try
        {
            // Keep the configured currency symbol in floating combat text.
            // Example: +0.001 ARKOS or -0.001 ARKOS.
            var formatted =
                cfg.Format(amountAtomic);

            var floatingText =
                isGain
                    ? $"+{formatted}"
                    : $"-{formatted}";

            // Bright arcade-style economy feedback.
            // Packet 119 uses Terraria's native string combat text.
            var color =
                isGain
                    ? new Microsoft.Xna.Framework.Color(
                        80,
                        255,
                        100)
                    : new Microsoft.Xna.Framework.Color(
                        255,
                        70,
                        70);

            // Position the combat text slightly above the player's head.
            var centerX =
                player.TPlayer.Center.X;

            var aboveHeadY =
                player.TPlayer.position.Y - 12f;

            NetMessage.SendData(
                119,
                player.Index,
                -1,
                NetworkText.FromLiteral(floatingText),
                unchecked((int)color.PackedValue),
                centerX,
                aboveHeadY);
        }
        catch (Exception ex)
        {
            // Floating text is cosmetic only. Never allow a visual failure
            // to interfere with the economy transaction itself.
            TShock.Log.ConsoleWarn(
                $"[ArkoviaEconomy] Floating currency text failed " +
                $"for {player.Name}: {ex.Message}");
        }
    }

    private static void SendRewardMessage(
        TSPlayer player,
        EconomyConfig cfg,
        string message)
    {
        var mode =
            cfg.GameplayEconomy.DefaultBroadcastMode?
                .Trim()
                .ToLowerInvariant();

        switch (mode)
        {
            case "silent":
                return;

            case "global":
                TSPlayer.All.SendSuccessMessage(
                    message);
                return;

            case "nearby":
            {
                // 50 Terraria tiles = 800 pixels.
                const float radiusPixels = 800f;
                const float radiusSquared =
                    radiusPixels * radiusPixels;

                foreach (var nearby in TShock.Players)
                {
                    if (nearby == null ||
                        !nearby.Active ||
                        !nearby.RealPlayer)
                    {
                        continue;
                    }

                    var dx =
                        nearby.X - player.X;

                    var dy =
                        nearby.Y - player.Y;

                    if ((dx * dx) + (dy * dy) <=
                        radiusSquared)
                    {
                        nearby.SendSuccessMessage(
                            message);
                    }
                }

                return;
            }

            case "playeronly":
            default:
                player.SendSuccessMessage(
                    message);
                return;
        }
    }

    // ---------------------------------------------------------------------
    // PLAYER DEATH / PVP
    // ---------------------------------------------------------------------

    private void OnKillMe(
        object? sender,
        GetDataHandlers.KillMeEventArgs args)
    {
        try
        {
            var cfg = _config();

            if (!cfg.GameplayEconomy.Enabled)
                return;

            var victim = args.Player;

            if (victim == null ||
                !victim.Active ||
                !victim.RealPlayer ||
                !victim.IsLoggedIn ||
                victim.Account == null)
            {
                return;
            }

            if (args.Pvp)
            {
                HandlePvpDeath(
                    victim,
                    args,
                    cfg);

                return;
            }

            HandleNormalDeath(
                victim,
                cfg);
        }
        catch (Exception ex)
        {
            TShock.Log.Error(
                $"[ArkoviaEconomy] Gameplay death handler failed: {ex}");
        }
    }

    private void HandleNormalDeath(
        TSPlayer player,
        EconomyConfig cfg)
    {
        var deathCfg =
            cfg.GameplayEconomy.Death;

        if (!deathCfg.Enabled)
            return;

        var now =
            DateTime.UtcNow;

        var userId =
            player.Account.ID;

        if (_lastDeathPenaltyUtc.TryGetValue(
                userId,
                out var last) &&
            (now - last).TotalSeconds <
            deathCfg.CooldownSeconds)
        {
            return;
        }

        var requestedAtomic =
            cfg.ToAtomic(
                deathCfg.Penalty);

        var protectedAtomic =
            cfg.ToAtomic(
                deathCfg.MinimumProtectedBalance);

        if (requestedAtomic <= 0)
            return;

        var account =
            _economy.GetOrCreatePlayer(
                userId,
                player.Name);

        var referenceId =
            $"death:{userId}:" +
            $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:" +
            $"{Guid.NewGuid():N}";

        var actualLoss =
            _economy.ApplyGameplayLoss(
                account,
                requestedAtomic,
                protectedAtomic,
                "player_death",
                referenceId,
                "Gameplay death penalty",
                player.Name);

        _lastDeathPenaltyUtc[userId] =
            now;

        if (actualLoss > 0)
        {
            ShowFloatingCurrencyChange(
                player,
                cfg,
                actualLoss,
                false);

            player.SendErrorMessage(
                $"You lost {cfg.Format(actualLoss)} after dying.");
        }
        else if (deathCfg.ShowZeroBalanceMessage)
        {
            player.SendInfoMessage(
                $"No {cfg.CurrencySymbol} was lost because " +
                "you have no spendable wallet balance.");
        }
    }

    private void HandlePvpDeath(
        TSPlayer victim,
        GetDataHandlers.KillMeEventArgs args,
        EconomyConfig cfg)
    {
        var pvpCfg =
            cfg.GameplayEconomy.PvP;

        if (!pvpCfg.Enabled)
            return;

        var victimUserId =
            victim.Account.ID;

        var now =
            DateTime.UtcNow;

        if (_lastPvpPenaltyUtc.TryGetValue(
                victimUserId,
                out var last) &&
            (now - last).TotalSeconds <
            pvpCfg.CooldownSeconds)
        {
            return;
        }

        var killerIndex =
            GetSourcePlayerIndex(
                args.PlayerDeathReason);

        if (killerIndex < 0 ||
            killerIndex >= TShock.Players.Length ||
            killerIndex == victim.Index)
        {
            TShock.Log.ConsoleWarn(
                $"[ArkoviaEconomy] PvP death for {victim.Name} " +
                "had no valid killer attribution. No currency moved.");

            return;
        }

        var killer =
            TShock.Players[killerIndex];

        if (killer == null ||
            !killer.Active ||
            !killer.RealPlayer ||
            !killer.IsLoggedIn ||
            killer.Account == null)
        {
            return;
        }

        if (killer.Account.ID ==
            victimUserId)
        {
            return;
        }

        var requestedAtomic =
            cfg.ToAtomic(
                pvpCfg.Penalty);

        var protectedAtomic =
            cfg.ToAtomic(
                pvpCfg.MinimumProtectedBalance);

        if (requestedAtomic <= 0)
            return;

        var victimAccount =
            _economy.GetOrCreatePlayer(
                victimUserId,
                victim.Name);

        var killerAccount =
            _economy.GetOrCreatePlayer(
                killer.Account.ID,
                killer.Name);

        var referenceId =
            $"pvp:{victimUserId}:{killer.Account.ID}:" +
            $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:" +
            $"{Guid.NewGuid():N}";

        var result =
            _economy.ApplyGameplayPvpLoss(
                victimAccount,
                killerAccount,
                requestedAtomic,
                protectedAtomic,
                pvpCfg.WinnerPercent,
                referenceId,
                "player_pvp_death",
                victim.Name);

        _lastPvpPenaltyUtc[victimUserId] =
            now;

        if (result.ActualLossAtomic <= 0)
        {
            victim.SendInfoMessage(
                $"No {cfg.CurrencySymbol} was lost because " +
                "you have no spendable wallet balance.");

            return;
        }

        ShowFloatingCurrencyChange(
            victim,
            cfg,
            result.ActualLossAtomic,
            false);

        victim.SendErrorMessage(
            $"You lost {cfg.Format(result.ActualLossAtomic)} " +
            $"in PvP to {killer.Name}.");

        if (result.WinnerAmountAtomic > 0)
        {
            ShowFloatingCurrencyChange(
                killer,
                cfg,
                result.WinnerAmountAtomic,
                true);

            killer.SendSuccessMessage(
                $"You earned {cfg.Format(result.WinnerAmountAtomic)} " +
                $"for defeating {victim.Name}.");
        }
    }

    private static int GetSourcePlayerIndex(
        PlayerDeathReason reason)
    {
        if (SourcePlayerIndexField == null)
            return -1;

        try
        {
            var value =
                SourcePlayerIndexField.GetValue(
                    reason);

            return value is int i
                ? i
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    public void Dispose()
    {
        if (!_registered)
            return;

        GetDataHandlers.KillMe.UnRegister(
            OnKillMe);

        ServerApi.Hooks.NpcStrike.Deregister(
            _plugin,
            OnNpcStrike);

        ServerApi.Hooks.NpcKilled.Deregister(
            _plugin,
            OnNpcKilled);

        On.Terraria.GameContent.Events.DD2Event.StartInvasion -=
            OnDd2StartInvasion;

        On.Terraria.GameContent.Events.DD2Event.ReportLoss -=
            OnDd2ReportLoss;

        On.Terraria.GameContent.Events.DD2Event.WinInvasionInternal -=
            OnDd2WinInvasionInternal;

        On.Terraria.GameContent.Events.DD2Event.StopInvasion -=
            OnDd2StopInvasion;

        _lastDeathPenaltyUtc.Clear();
        _lastPvpPenaltyUtc.Clear();
        _lastNpcAttacker.Clear();

        lock (_dd2Sync)
        {
            _dd2DamageByUserId.Clear();
            _dd2SessionActive = false;
            _dd2VictoryPending = false;
            _dd2LossReported = false;
            _dd2Difficulty = 0;
        }

        _registered = false;
    }
}
