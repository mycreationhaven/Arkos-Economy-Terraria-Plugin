using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using Newtonsoft.Json;

namespace ArkoviaEconomy.Progression;

public sealed class ProgressionState
{
    public int Level {get;set;} = 1;
    public long Experience {get;set;}
    public long ActiveMinutes {get;set;}
    public DateTime LastActivity {get;set;}
    public DateTime LastRankChange {get;set;}
    public DateTime LastDeath {get;set;}
    public string Day {get;set;} = "";
    public int KillsToday {get;set;}
    public bool AdminApproved {get;set;}
    public string Job {get;set;} = "";
    public string Quest {get;set;} = "";
    public Dictionary<string,int> Counts {get;set;} = new();
    public Dictionary<string,int> Claims {get;set;} = new();
    public HashSet<int> RewardedLevels {get;set;} = new();
    public List<RankItem> PendingItems {get;set;} = new();
}

/// <summary>All progression and wallet mutations share the economy lock and one SQL transaction.</summary>
public sealed class ProgressionService(EconomyDatabase db, EconomyService economy, Func<EconomyConfig> config)
{
    private readonly Dictionary<int,ProgressionState> _permissionStates = new();
    public ProgressionState Get(int userId) => economy.Locked(()=>Read(userId));
    private ProgressionState Read(int userId) => JsonConvert.DeserializeObject<ProgressionState>(db.GetState(Key(userId)) ?? "{}")!;
    private static string Key(int id) => "progression:"+id;
    public RankDefinition Rank(int level) => config().Progression.Ranks.Single(r=>r.Level==level);
    private T Change<T>(int userId, Func<ProgressionState, SettlementUnit,T> action) => economy.Locked(()=>
    {
        if(!config().Progression.Enabled) throw new InvalidOperationException("Progression is disabled.");
        var old = db.GetState(Key(userId));
        var state = JsonConvert.DeserializeObject<ProgressionState>(old ?? "{}")!;
        var result = db.Atomic(tx=>
        {
            var result = action(state,tx);
            var json = JsonConvert.SerializeObject(state);
            if(old is null) tx.Execute("INSERT INTO ArkoviaEconomyState (StateKey,StateValue,UpdatedUtc) VALUES (@p0,@p1,@p2)",Key(userId),json,DateTime.UtcNow.ToString("O"));
            else if(tx.Execute("UPDATE ArkoviaEconomyState SET StateValue=@p0,UpdatedUtc=@p1 WHERE StateKey=@p2 AND StateValue=@p3",json,DateTime.UtcNow.ToString("O"),Key(userId),old)!=1)
                throw new InvalidOperationException("Progression changed; retry.");
            return result;
        });
        _permissionStates.Remove(userId);
        return result;
    });
    private static void Day(ProgressionState s, DateTime now)
    {
        var day=now.ToString("yyyy-MM-dd");
        if(s.Day==day)return;
        s.Day=day;s.KillsToday=0;s.Claims.Clear(); // Progress carries over; claim quotas reset at UTC midnight.
    }
    public bool Kill(int userId,int npcId,DateTime now) => Change(userId,(s,tx)=>
    {
        Day(s,now);
        var cfg=config().Progression;
        if(s.KillsToday>=cfg.DailyKillLimit || (now-s.LastActivity).TotalSeconds<cfg.KillCooldownSeconds)return false;
        if(s.LastActivity.Date!=now.Date || (long)(s.LastActivity.TimeOfDay.TotalMinutes)!=(long)now.TimeOfDay.TotalMinutes)
            s.ActiveMinutes=checked(s.ActiveMinutes+1);
        s.LastActivity=now;s.KillsToday++;s.Experience=checked(s.Experience+1);
        foreach(var (kind,id,list) in new[]{("job",s.Job,cfg.Jobs),("quest",s.Quest,cfg.Quests)})
        {
            var a=list.FirstOrDefault(x=>x.Id==id);
            if(a is null || (a.NpcIds.Count>0 && !a.NpcIds.Contains(npcId)))continue;
            var key=kind+":"+id;
            if(s.Claims.GetValueOrDefault(key)>=a.DailyLimit)continue;
            s.Counts[key]=Math.Min(a.RequiredKills,s.Counts.GetValueOrDefault(key)+1);
        }
        return true;
    });
    public void Select(int userId,bool job,string id) => Change(userId,(s,tx)=>
    {
        var list=job?config().Progression.Jobs:config().Progression.Quests;
        if(id!="leave" && !list.Any(x=>x.Id==id))throw new InvalidOperationException("Unknown activity ID.");
        if(job)s.Job=id=="leave"?"":id;else s.Quest=id=="leave"?"":id;
        return true;
    });
    public long Claim(int userId,bool job,DateTime now) => Change(userId,(s,tx)=>
    {
        Day(s,now);
        var id=job?s.Job:s.Quest;
        var a=(job?config().Progression.Jobs:config().Progression.Quests).FirstOrDefault(x=>x.Id==id)
            ?? throw new InvalidOperationException("Select an activity first.");
        var key=(job?"job:":"quest:")+id;
        if(s.Counts.GetValueOrDefault(key)<a.RequiredKills || s.Claims.GetValueOrDefault(key)>=a.DailyLimit)
            throw new InvalidOperationException("Objective incomplete or daily claim limit reached.");
        var amount=config().ToAtomic(a.Reward);
        Move(tx,userId,amount,false,key);
        s.Experience=checked(s.Experience+a.Experience);s.Counts[key]=0;s.Claims[key]=s.Claims.GetValueOrDefault(key)+1;
        return amount;
    });
    public RankDefinition RankUp(int userId,DateTime now) => Change(userId,(s,tx)=>
    {
        if(s.Level>=100)throw new InvalidOperationException("Already at maximum rank.");
        var rank=Rank(s.Level+1);
        if(s.Experience<rank.Experience || s.ActiveMinutes<rank.ActiveMinutes)
            throw new InvalidOperationException($"Requires {rank.Experience} total XP and {rank.ActiveMinutes} combat-active minutes.");
        if((now-s.LastRankChange).TotalHours<config().Progression.RankCooldownHours)
            throw new InvalidOperationException("Rank change cooldown is still active.");
        if(rank.Level==100 && config().Progression.RequireAdminApprovalForLevel100 && !s.AdminApproved)
            throw new InvalidOperationException("Rank 100 requires server-owner approval.");
        Move(tx,userId,config().ToAtomic(rank.Cost),true,"rank:"+rank.Level);
        s.Level=rank.Level;s.LastRankChange=now;
        if(s.RewardedLevels.Add(rank.Level))s.PendingItems.AddRange(rank.Items);
        return rank;
    });
    public int Demote(int userId,DateTime now) => Change(userId,(s,tx)=>
    {
        // TShock may receive duplicate death packets; Terraria respawn takes longer than this interval.
        if((now-s.LastDeath).TotalSeconds<2)return 0;
        s.LastDeath=now;
        if(s.Level<=1)return 0;
        s.Level--;s.LastRankChange=now;
        if(s.Level==99)s.AdminApproved=false;
        return s.Level;
    });
    public void Approve(int userId,bool approved) => Change(userId,(s,tx)=>{s.AdminApproved=approved;return true;});
    public bool Grants(int userId,string permission) => economy.Locked(()=>
    {
        if(!config().Progression.Enabled)return false;
        if(!_permissionStates.TryGetValue(userId,out var s))_permissionStates[userId]=s=Read(userId);
        return config().Progression.Ranks.Where(r=>r.Level<=s.Level &&
            (r.Level!=100 || !config().Progression.RequireAdminApprovalForLevel100 || s.AdminApproved))
            .SelectMany(r=>r.Permissions).Any(p=>p=="*" || p==permission || p.EndsWith(".*",StringComparison.Ordinal) && permission.StartsWith(p[..^1],StringComparison.Ordinal));
    });
    // Mark before dispatch: never duplicate valuable items after a crash. Failed deliveries need owner reconciliation.
    public List<RankItem> TakeItems(int userId) => Change(userId,(s,tx)=>
    {
        var items=s.PendingItems.ToList();s.PendingItems.Clear();return items;
    });
    private void Move(SettlementUnit tx,int userId,long amount,bool charge,string reason)
    {
        if(amount<0 || charge && amount==0)throw new InvalidOperationException("Configured amount is below currency precision.");
        if(amount==0)return;
        var account=db.GetPlayerAccount(userId) ?? throw new InvalidOperationException("Economy account missing.");
        var treasury=economy.GetTreasury();
        var from=charge?account:treasury;var to=charge?treasury:account;
        var next=checked(to.WalletAtomic+amount);
        if(from.Frozen || to.Frozen || from.WalletAtomic<amount || !charge && next>config().ToAtomic(config().MaximumPlayerBalance))
            throw new InvalidOperationException("Insufficient wallet/treasury funds, frozen account, or balance limit.");
        tx.Wallet(from,from.WalletAtomic-amount);tx.Wallet(to,next);
        tx.Ledger(Guid.NewGuid().ToString("N"),from.Id,to.Id,amount,charge?"rank_purchase":"activity_reward",reason,account.Name);
    }
}
