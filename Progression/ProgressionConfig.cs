namespace ArkoviaEconomy.Progression;

public sealed class ProgressionConfig
{
    public bool Enabled { get; set; } = true;
    public int DailyKillLimit { get; set; } = 500;
    public int KillCooldownSeconds { get; set; } = 5;
    public int RankCooldownHours { get; set; } = 12;
    public bool RequireAdminApprovalForLevel100 { get; set; } = true;
    public List<RankDefinition> Ranks { get; set; } = Enumerable.Range(1,100).Select(level => new RankDefinition
    {
        Level = level, Name = level == 100 ? "Server Admin" : $"Level {level}",
        Cost = level == 1 ? 0 : 0.01m * (level-1)*(level-1),
        Experience = 25L*(level-1)*(level-1), ActiveMinutes = 10L*(level-1)*(level-1),
        Permissions = level == 100 ? new() { "*" } : new()
    }).ToList();
    public List<ActivityDefinition> Quests { get; set; } = new()
    {
        new() { Id="daily-hunt", Name="Daily monster hunt", RequiredKills=100, Reward=5, Experience=100, DailyLimit=1 },
        new() { Id="slime-patrol", Name="Slime patrol", NpcIds=new(){1}, RequiredKills=50, Reward=2, Experience=50, DailyLimit=1 }
    };
    public List<ActivityDefinition> Jobs { get; set; } = new()
    {
        new() { Id="hunter", Name="Monster hunter", RequiredKills=25, Reward=1, Experience=25, DailyLimit=10 },
        new() { Id="slimekeeper", Name="Slime keeper", NpcIds=new(){1}, RequiredKills=20, Reward=1, Experience=20, DailyLimit=10 }
    };
    public void Validate()
    {
        if (DailyKillLimit is < 1 or > 100000 || KillCooldownSeconds < 1 || RankCooldownHours < 0 ||
            Ranks.Count != 100 || !Ranks.Select(r=>r.Level).Order().SequenceEqual(Enumerable.Range(1,100)))
            throw new InvalidOperationException("Progression requires unique levels 1-100 and positive activity limits.");
        foreach(var r in Ranks)
            if (r.Cost < 0 || (r.Level > 1 && r.Cost <= 0) || r.Experience < 0 || r.ActiveMinutes < 0 || string.IsNullOrWhiteSpace(r.Name) ||
                r.Items.Count > 20 || r.Permissions.Count > 100 || r.Permissions.Any(string.IsNullOrWhiteSpace) || r.Items.Any(i=>i.ItemId<=0 || i.ItemId>=Terraria.ID.ItemID.Count || i.Prefix>=Terraria.ID.PrefixID.Count || i.Stack is <1 or >9999 || i.Prefix is <0 or >255))
                throw new InvalidOperationException("Invalid rank requirement or reward.");
        foreach(var list in new[]{Quests,Jobs})
        {
            if(list.Select(x=>x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Count || list.Count>100)
                throw new InvalidOperationException("Activity IDs must be unique (maximum 100 per type).");
            foreach(var a in list)
                if(!System.Text.RegularExpressions.Regex.IsMatch(a.Id,"^[a-z0-9-]{1,40}$") || a.RequiredKills<=0 || a.Reward<0 || a.Experience<0 || a.DailyLimit<1 || a.NpcIds.Any(x=>x<0))
                    throw new InvalidOperationException("Invalid quest/job definition.");
        }
    }
}
public sealed class RankDefinition
{
    public int Level {get;set;}
    public string Name {get;set;} = "";
    public decimal Cost {get;set;}
    public long Experience {get;set;}
    public long ActiveMinutes {get;set;}
    public List<string> Permissions {get;set;} = new();
    public List<RankItem> Items {get;set;} = new();
}
public sealed record RankItem(int ItemId, int Stack=1, int Prefix=0);
public sealed class ActivityDefinition
{
    public string Id {get;set;} = "";
    public string Name {get;set;} = "";
    public List<int> NpcIds {get;set;} = new(); // Empty means any eligible hostile NPC.
    public int RequiredKills {get;set;} = 25;
    public decimal Reward {get;set;} = 1;
    public long Experience {get;set;} = 25;
    public int DailyLimit {get;set;} = 1;
}
