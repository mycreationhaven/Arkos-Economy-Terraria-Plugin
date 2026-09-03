using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ArkoviaEconomy.Progression;

public sealed class ProgressionHandler : IDisposable
{
    private readonly TerrariaPlugin _plugin;
    private readonly ProgressionService _service;
    private readonly EconomyService _economy;
    private readonly Func<EconomyConfig> _config;
    private readonly Dictionary<int,(NPC Npc,int UserId)> _attackers=new();
    private readonly List<Command> _commands=new();
    public ProgressionHandler(TerrariaPlugin plugin,ProgressionService service,EconomyService economy,Func<EconomyConfig> config)
    { _plugin=plugin;_service=service;_economy=economy;_config=config; }
    public void Register()
    {
        // Typed item grants only; no interpolated console commands or replacement of staff groups.
        foreach(var item in _config().Progression.Ranks.SelectMany(r=>r.Items))
            if(item.ItemId>=Terraria.ID.ItemID.Count || item.Prefix>=Terraria.ID.PrefixID.Count)
                throw new InvalidOperationException("Rank item ID/prefix is not valid for this Terraria version.");
        ServerApi.Hooks.NpcSpawn.Register(_plugin,Spawn);
        ServerApi.Hooks.NpcStrike.Register(_plugin,Strike);
        ServerApi.Hooks.NpcKilled.Register(_plugin,Killed);
        GetDataHandlers.KillMe.Register(Death);
        PlayerHooks.PlayerPermission+=Permission;
        _commands.Add(new Command("arkoviaeconomy.rank",RankCommand,"rank","rankup"));
        _commands.Add(new Command("arkoviaeconomy.quests",a=>Activity(a,false),"quests","quest"));
        _commands.Add(new Command(Permissions.Jobs,a=>Activity(a,true),"jobs","job"));
        _commands.Add(new Command(Permissions.Admin,a=>Run(a,()=>
        {
            // Use base group permission to keep rank-earned '*' from approving itself or friends.
            if(a.Player.RealPlayer && !a.Player.Group.HasPermission("arkoviaeconomy.rank.approve"))
                throw new InvalidOperationException("Requires base-group arkoviaeconomy.rank.approve permission.");
            if(a.Parameters.Count!=2 || !int.TryParse(a.Parameters[0],out var id) || id<=0 ||
                a.Parameters[1] is not ("approve" or "revoke"))
                throw new InvalidOperationException("Usage: /rankadmin <TShock account ID> approve|revoke");
            _service.Approve(id,a.Parameters[1]=="approve");
            EconomyLog.Info($"Rank 100 approval {a.Parameters[1]} for account {id} by {a.Player.Name}.");
            a.Player.SendSuccessMessage("Rank 100 approval updated. Rank requirements and fee still apply.");
        },false),"rankadmin"));
        TShockAPI.Commands.ChatCommands.AddRange(_commands);
    }
    private static bool Eligible(TSPlayer? p)=>p is {Active:true,RealPlayer:true,IsLoggedIn:true,Account:not null};
    private void Spawn(NpcSpawnEventArgs args) => _attackers.Remove(args.NpcId);
    private void Strike(NpcStrikeEventArgs args)
    {
        if(!_config().Progression.Enabled || args.Npc is null || args.Player is null || args.Damage<=0)return;
        var i=args.Player.whoAmI;
        if(i<0 || i>=TShock.Players.Length)return;
        var p=TShock.Players[i];var n=args.Npc;
        if(!Eligible(p) || n.friendly || n.townNPC || n.SpawnedFromStatue || n.lifeMax<5)return;
        _attackers[n.whoAmI]=(n,p.Account.ID);
    }
    private void Killed(NpcKilledEventArgs args)
    {
        var n=args.npc;
        if(n is null || !_attackers.Remove(n.whoAmI,out var hit) || !ReferenceEquals(hit.Npc,n) ||
            n.friendly || n.townNPC || n.SpawnedFromStatue || n.lifeMax<5 || !_config().Progression.Enabled)return;
        var p=TShock.Players.FirstOrDefault(x=>Eligible(x)&&x.Account.ID==hit.UserId);
        if(p is null)return;
        try { _economy.GetOrCreatePlayer(p.Account.ID,p.Account.Name);_service.Kill(p.Account.ID,n.netID,DateTime.UtcNow); }
        catch(Exception ex){EconomyLog.Error("Progression kill failed: "+ex);}
    }
    private void Death(object? sender,GetDataHandlers.KillMeEventArgs args)
    {
        if(args.Handled || !Eligible(args.Player) || !_config().Progression.Enabled)return;
        try
        {
            var level=_service.Demote(args.Player.Account.ID,DateTime.UtcNow);
            if(level>0)TSPlayer.All.SendInfoMessage($"{args.Player.Name} died and was demoted to {_service.Rank(level).Name} (level {level}).");
        }
        catch(Exception ex){EconomyLog.Error("Rank demotion failed: "+ex);}
    }
    private void Permission(PlayerPermissionEventArgs args)
    {
        if(args.Result!=PermissionHookResult.Unhandled || !Eligible(args.Player))return;
        try { if(_service.Grants(args.Player.Account.ID,args.Permission))args.Result=PermissionHookResult.Granted; }
        catch(Exception ex){EconomyLog.Error("Rank permissions unavailable: "+ex.GetType().Name);}
    }
    private void Run(CommandArgs a,Action action,bool player=true)
    {
        try
        {
            if(player && !Eligible(a.Player))throw new InvalidOperationException("Log in to use progression.");
            if(!_config().Progression.Enabled)throw new InvalidOperationException("Progression is disabled.");
            if(player)_economy.GetOrCreatePlayer(a.Player.Account.ID,a.Player.Account.Name);
            action();
        }
        catch(InvalidOperationException ex){a.Player.SendErrorMessage(ex.Message);}
        catch(Exception ex){EconomyLog.Error("Progression command failed: "+ex);a.Player.SendErrorMessage("Progression operation failed; see the plugin log.");}
    }
    private void RankCommand(CommandArgs a)=>Run(a,()=>
    {
        var id=a.Player.Account.ID;
        var verb=a.Parameters.FirstOrDefault() ?? (a.Message.TrimStart('/').StartsWith("rankup",StringComparison.OrdinalIgnoreCase)?"up":"status");
        if(verb=="up")
        {
            var rank=_service.RankUp(id,DateTime.UtcNow);
            EconomyLog.Info($"Account {id} purchased rank {rank.Level}.");
            TSPlayer.All.SendSuccessMessage($"{a.Player.Name} reached {rank.Name} (level {rank.Level}) for {_config().Format(_config().ToAtomic(rank.Cost))}!");
            a.Player.SendInfoMessage("Use /rank claim for any one-time item rewards.");return;
        }
        if(verb=="claim")
        {
            if(a.Player.TPlayer.dead)throw new InvalidOperationException("Respawn before claiming items.");
            var items=_service.Get(id).PendingItems;
            if(items.Any(i=>i.ItemId>=Terraria.ID.ItemID.Count || i.Prefix>=Terraria.ID.PrefixID.Count))
                throw new InvalidOperationException("Pending item definition requires administrator correction.");
            if(items.Count==0)throw new InvalidOperationException("No pending rank items.");
            // Audit the delivery intent before removing it durably; owner can reconcile an interrupted delivery.
            EconomyLog.Info($"Rank item delivery for account {id}: "+Newtonsoft.Json.JsonConvert.SerializeObject(items));
            foreach(var item in _service.TakeItems(id))a.Player.GiveItem(item.ItemId,item.Stack,item.Prefix);
            a.Player.SendSuccessMessage("Rank item rewards delivered.");return;
        }
        var s=_service.Get(id);
        a.Player.SendInfoMessage($"{_service.Rank(s.Level).Name}: {s.Experience} XP, {s.ActiveMinutes} combat-active minutes. /rank up | /rank claim");
        if(s.Level<100){var next=_service.Rank(s.Level+1);a.Player.SendInfoMessage($"Next: {next.Experience} XP, {next.ActiveMinutes} minutes, {_config().Format(_config().ToAtomic(next.Cost))}; {_config().Progression.RankCooldownHours}h between rank changes.");}
    });
    private void Activity(CommandArgs a,bool job)=>Run(a,()=>
    {
        var id=a.Player.Account.ID;var kind=job?"job":"quest";
        var verb=a.Parameters.FirstOrDefault()??"list";
        if(verb is "join" or "accept" && a.Parameters.Count==2){_service.Select(id,job,a.Parameters[1]);a.Player.SendSuccessMessage("Activity selected.");return;}
        if(verb=="leave"){_service.Select(id,job,"leave");a.Player.SendSuccessMessage("Activity left; progress retained.");return;}
        if(verb=="claim"){var amount=_service.Claim(id,job,DateTime.UtcNow);a.Player.SendSuccessMessage("Reward: "+_config().Format(amount));return;}
        var s=_service.Get(id);var selected=job?s.Job:s.Quest;
        a.Player.SendInfoMessage($"/{kind} {(job?"join":"accept")} <id> | claim | leave. Selected: {selected}");
        foreach(var def in job?_config().Progression.Jobs:_config().Progression.Quests)
            a.Player.SendInfoMessage($"{def.Id}: {def.Name}; {s.Counts.GetValueOrDefault(kind+":"+def.Id)}/{def.RequiredKills} kills; {_config().Format(_config().ToAtomic(def.Reward))}, {def.Experience} XP; {def.DailyLimit} claims/day.");
    });
    public void Dispose()
    {
        ServerApi.Hooks.NpcSpawn.Deregister(_plugin,Spawn);ServerApi.Hooks.NpcStrike.Deregister(_plugin,Strike);ServerApi.Hooks.NpcKilled.Deregister(_plugin,Killed);
        GetDataHandlers.KillMe.UnRegister(Death);PlayerHooks.PlayerPermission-=Permission;
        foreach(var c in _commands)TShockAPI.Commands.ChatCommands.Remove(c);
        _attackers.Clear();
    }
}
