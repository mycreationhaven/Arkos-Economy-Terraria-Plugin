using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using TShockAPI;

namespace ArkoviaEconomy.Gameplay;

// Events observed halfway through a server restart begin fresh participation tracking.
// A shutdown/forced event cancellation never earns a completion pool.
public sealed class WorldEventTracker(TerrariaPlugin plugin, EconomyService economy, Func<EconomyConfig> config) : IDisposable
{
    private sealed class Encounter(string name)
    {
        public string Name = name;
        public string Id = "event:" + Guid.NewGuid().ToString("N");
        public DateTime Started = DateTime.UtcNow;
        public Dictionary<int, long> Damage = new();
    }
    private readonly Dictionary<string, Encounter> _active = new();
    private int _lastInvasion;
    private bool _lastDay;
    private static readonly Dictionary<string, HashSet<int>> EventNpcs = new()
    {
        ["BloodMoon"] = Types("BloodZombie", "Drippler", "TheGroom", "TheBride", "ZombieMerman", "EyeballFlyingFish", "BloodEelHead", "BloodEelBody", "BloodEelTail", "BloodSquid", "GoblinShark"),
        ["SolarEclipse"] = Types("Frankenstein", "SwampThing", "Vampire", "VampireBat", "Reaper", "Eyezor", "CreatureFromTheDeep", "Fritz", "ThePossessed", "Butcher", "DeadlySphere", "DrManFly", "Mothron", "MothronSpawn", "Psycho", "Nailhead"),
        ["PumpkinMoon"] = Types("MourningWood", "Pumpking", "PumpkingBlade", "Splinterling", "Hellhound", "Poltergeist", "HeadlessHorseman", "Scarecrow1", "Scarecrow2", "Scarecrow3", "Scarecrow4", "Scarecrow5", "Scarecrow6", "Scarecrow7", "Scarecrow8", "Scarecrow9", "Scarecrow10"),
        ["FrostMoon"] = Types("IceQueen", "SantaNK1", "Everscream", "ElfCopter", "Flocko", "Nutcracker", "NutcrackerSpinning", "Krampus", "Yeti", "ZombieElf", "ZombieElfBeard", "ZombieElfGirl", "GingerbreadMan", "PresentMimic")
    };
    private static HashSet<int> Types(params string[] names) => names.Select(n => typeof(NPCID).GetField(n)?.GetRawConstantValue())
        .Where(v => v is not null).Select(Convert.ToInt32).ToHashSet();
    public void Register() { _lastDay = Main.dayTime; ServerApi.Hooks.GameUpdate.Register(plugin, Update); }
    private static string? InvasionName(int type) => type switch
    { 1 => "GoblinArmy", 2 => "FrostLegion", 3 => "PirateInvasion", 4 => "MartianMadness", _ => null };
    private void Update(EventArgs args)
    {
        try
        {
            while (economy.TryDequeueEventNotice(out var notice))
            {
                var player = TShock.Players.FirstOrDefault(p => p is { Active: true, IsLoggedIn: true } && p.Account?.ID == notice.UserId);
                player?.SendSuccessMessage($"You earned {config().Format(notice.Atomic)} for completing {notice.Event}.");
            }
            var running = new HashSet<string>();
            if (InvasionName(Main.invasionType) is string invasion && Main.invasionSize > 0) running.Add(invasion);
            if (Main.bloodMoon) running.Add("BloodMoon");
            if (Main.eclipse) running.Add("SolarEclipse");
            if (Main.pumpkinMoon) running.Add("PumpkinMoon");
            if (Main.snowMoon) running.Add("FrostMoon");
            foreach (var name in _active.Keys.Except(running).ToArray())
            {
                var encounter = _active[name]; _active.Remove(name);
                var invasionWon = name == InvasionName(_lastInvasion) && Main.invasionSize <= 0;
                var naturalEnd = name == "SolarEclipse" ? _lastDay && !Main.dayTime : !_lastDay && Main.dayTime;
                if (invasionWon || (EventNpcs.ContainsKey(name) && naturalEnd)) Finish(encounter);
            }
            if (config().GameplayEconomy.Enabled && config().EventRewards.Enabled)
            {
                foreach (var name in running) if (!_active.ContainsKey(name)) _active[name] = new(name);
            }
            else _active.Clear();
            _lastInvasion = Main.invasionType; _lastDay = Main.dayTime;
        }
        catch (Exception ex) { ArkoviaEconomy.Core.EconomyLog.Error("[ArkoviaEconomy] Event tracking: " + ex.Message); }
    }
    public void Track(int userId, NPC npc, int damage)
    {
        if (damage <= 0 || npc.SpawnedFromStatue || npc.friendly || npc.townNPC) return;
        var contribution = Math.Min(damage, Math.Max(0, npc.life));
        foreach (var e in _active.Values)
        {
            var member = EventNpcs.TryGetValue(e.Name, out var types) ? types.Contains(npc.type)
                : e.Name == InvasionName(NPC.GetNPCInvasionGroup(npc.type));
            if (!member || contribution <= 0) continue;
            var old = e.Damage.GetValueOrDefault(userId);
            e.Damage[userId] = old > long.MaxValue - contribution ? long.MaxValue : old + contribution;
        }
    }
    private void Finish(Encounter e)
    {
        var cfg = config();
        if (!cfg.GameplayEconomy.Enabled || !cfg.EventRewards.Enabled ||
            (DateTime.UtcNow - e.Started).TotalSeconds < cfg.EventRewards.MinimumDurationSeconds) return;
        var pool = cfg.ToAtomic(cfg.EventRewards.Pools.GetValueOrDefault(e.Name));
        if (economy.QueueEvent(e.Id, e.Name, pool, e.Damage))
            ArkoviaEconomy.Core.EconomyLog.Info($"[ArkoviaEconomy] {e.Name} completed; reward pool queued for atomic settlement.");
    }
    public void Dispose() { ServerApi.Hooks.GameUpdate.Deregister(plugin, Update); _active.Clear(); }
}
