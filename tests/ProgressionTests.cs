using ArkoviaEconomy.Config;
using ArkoviaEconomy.Core;
using ArkoviaEconomy.Database;
using ArkoviaEconomy.Progression;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

static class ProgressionTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool c){if(!c)throw new Exception("Progression check "+(checks+1));checks++;}
        void Reject(Action a){try{a();}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected progression rejection");}
        var path=Path.Combine(Path.GetTempPath(),"progression-"+Guid.NewGuid()+".sqlite");
        using var connection=new SqliteConnection("Data Source="+path);connection.Open();
        var db=new EconomyDatabase(connection);db.EnsureSchema();
        var cfg=new EconomyConfig();cfg.Progression.Validate();
        var eco=new EconomyService(db,()=>cfg);var p=eco.GetOrCreatePlayer(20,"Ranker");var t=eco.GetTreasury();
        var service=new ProgressionService(db,eco,()=>cfg);
        var now=new DateTime(2026,9,3,12,0,0,DateTimeKind.Utc);
        Check(service.Get(20).Level==1);
        Reject(()=>service.RankUp(20,now));
        Check(service.Demote(20,now)==0);
        Check(service.Kill(20,1,now));Check(!service.Kill(20,1,now.AddSeconds(1)));
        Check(service.Get(20).Experience==1 && service.Get(20).ActiveMinutes==1);
        service.Select(20,true,"hunter");service.Select(20,false,"daily-hunt");
        cfg.Progression.Jobs[0].RequiredKills=2;cfg.Progression.Quests[0].RequiredKills=2;
        service.Kill(20,1,now.AddSeconds(5));service.Kill(20,1,now.AddSeconds(10));
        Reject(()=>service.Claim(20,true,now));
        Check(service.Get(20).Counts["job:hunter"]==2);
        db.SetBalances(t.Id,cfg.ToAtomic(1000),0);
        Check(service.Claim(20,true,now)==cfg.ToAtomic(1));
        Reject(()=>service.Claim(20,true,now));
        Check(service.Claim(20,false,now)==cfg.ToAtomic(5));
        Reject(()=>service.Claim(20,false,now));
        var r=cfg.Progression.Ranks[1];r.Experience=0;r.ActiveMinutes=0;r.Cost=1;r.Permissions.Add("test.perk");r.Items.Add(new(8,2));
        var bank=cfg.ToAtomic(50);db.SetBalances(p.Id,cfg.ToAtomic(100),bank);
        var treasury=eco.GetTreasury().WalletAtomic;
        Check(service.RankUp(20,now.AddHours(13)).Level==2);
        Check(db.GetAccountById(p.Id)!.WalletAtomic==cfg.ToAtomic(99) && db.GetAccountById(p.Id)!.BankAtomic==bank);
        Check(eco.GetTreasury().WalletAtomic==treasury+cfg.ToAtomic(1));
        Check(service.Grants(20,"test.perk"));
        Check(service.TakeItems(20).Count==1 && service.TakeItems(20).Count==0);
        Check(service.Demote(20,now.AddHours(14))==1);
        Check(!service.Grants(20,"test.perk"));
        Check(service.Demote(20,now.AddHours(14).AddSeconds(1))==0);
        Reject(()=>service.RankUp(20,now.AddHours(15)));
        Check(service.RankUp(20,now.AddHours(27)).Level==2 && service.TakeItems(20).Count==0);
        // Force a failure after wallet/ledger writes; every write, including the rank, must roll back.
        var r3=cfg.Progression.Ranks[2];r3.Experience=0;r3.ActiveMinutes=0;r3.Cost=1;
        var balance=db.GetAccountById(p.Id)!.WalletAtomic;treasury=eco.GetTreasury().WalletAtomic;
        using(var cmd=connection.CreateCommand()){cmd.CommandText="CREATE TRIGGER stop_progress BEFORE UPDATE ON ArkoviaEconomyState BEGIN SELECT RAISE(ABORT,'test'); END";cmd.ExecuteNonQuery();}
        try{service.RankUp(20,now.AddHours(40));throw new Exception("Expected SQL failure");}catch(SqliteException){checks++;}
        Check(service.Get(20).Level==2 && db.GetAccountById(p.Id)!.WalletAtomic==balance && eco.GetTreasury().WalletAtomic==treasury);
        using(var cmd=connection.CreateCommand()){cmd.CommandText="DROP TRIGGER stop_progress";cmd.ExecuteNonQuery();}
        // New service emulates restart, preserving progress, claim history and one-time items.
        service=new(db,eco,()=>cfg);Check(service.Get(20).Level==2 && service.TakeItems(20).Count==0);
        var admin=eco.GetOrCreatePlayer(21,"Candidate");db.SetBalances(admin.Id,cfg.ToAtomic(100),0);
        db.SetState("progression:21",JsonConvert.SerializeObject(new ProgressionState{Level=99,Experience=long.MaxValue,ActiveMinutes=long.MaxValue}));
        cfg.Progression.Ranks[99].Cost=1;
        Reject(()=>service.RankUp(21,now));service.Approve(21,true);
        Check(service.RankUp(21,now).Level==100 && service.Grants(21,"tshock.admin.kick"));
        service.Demote(21,now.AddSeconds(5));Check(!service.Grants(21,"tshock.admin.kick") && !service.Get(21).AdminApproved);
        Reject(()=>service.RankUp(21,now.AddHours(13)));
        cfg.Progression.DailyKillLimit=3;
        Check(!service.Kill(20,1,now.AddMinutes(1)));
        Check(service.Kill(20,1,now.AddDays(1)));
        cfg.Progression.Enabled=false;Check(!service.Grants(20,"test.perk"));Reject(()=>service.RankUp(20,now.AddDays(3)));
        cfg.Progression.Ranks[2].Cost=0;Reject(cfg.Progression.Validate);
        connection.Close();SqliteConnection.ClearAllPools();File.Delete(path);
        var logs=Path.Combine(Path.GetTempPath(),"arkovia-logs-"+Guid.NewGuid());EconomyLog.Initialize(logs);
        EconomyLog.Info("test\nline");EconomyLog.Warn("warning");EconomyLog.Error("error");
        var file=Directory.GetFiles(Path.Combine(logs,"logs")).Single();
        Check(File.ReadAllLines(file).Length==3 && File.ReadAllText(file).Contains("test\\nline"));Directory.Delete(logs,true);
        return checks;
    }
}
