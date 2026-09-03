using ArkoviaEconomy.Security;
static class PortalCodeTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool c){if(!c)throw new Exception("Portal code check "+(checks+1));checks++;}
        void Reject(Action a){try{a();}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected code rejection");}
        var now=DateTime.UtcNow;var codes=new PortalAccessCodes(()=>now);
        var code=codes.Issue(1,"Alice",now.AddMinutes(5));Check(code.Length==6&&code.All(char.IsDigit));
        Reject(()=>codes.Redeem("Bob",code));Check(codes.Redeem("alice",code).UserId==1);Reject(()=>codes.Redeem("Alice",code));
        code=codes.Issue(1,"Alice",now.AddMinutes(5));var wrong=code=="123456"?"654321":"123456";
        for(int i=0;i<5;i++)Reject(()=>codes.Redeem("Alice",wrong));Reject(()=>codes.Redeem("Alice",code));
        code=codes.Issue(1,"Alice",now.AddMinutes(5));now=now.AddMinutes(5);Reject(()=>codes.Redeem("Alice",code));
        codes.Issue(1,"OldName",now.AddMinutes(5));var next=codes.Issue(1,"NewName",now.AddMinutes(5));
        Reject(()=>codes.Redeem("OldName",next));Check(codes.Redeem("NewName",next).UserId==1);
        codes=new PortalAccessCodes(()=>now);code=codes.Issue(1,"Alice",now.AddMinutes(5));
        for(int i=0;i<60;i++){try{codes.Redeem("missing","000000");}catch(InvalidOperationException){}}
        Reject(()=>codes.Redeem("Alice",code));now=now.AddMinutes(1);Check(codes.Redeem("Alice",code).UserId==1);
        code=codes.Issue(1,"Alice",now.AddMinutes(5));int wins=0;
        Parallel.For(0,10,_=>{try{codes.Redeem("Alice",code);Interlocked.Increment(ref wins);}catch(InvalidOperationException){}});
        Check(wins==1);
        code=codes.Issue(1,"Alice",now.AddMinutes(5));codes.Clear();Reject(()=>codes.Redeem("Alice",code));
        return checks;
    }
}
